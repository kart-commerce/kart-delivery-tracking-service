using System.Text;
using System.Text.Json;
using Kart.Shared.Domain;
using KartDeliveryTrackingService.Application.Common.Interfaces;
using KartDeliveryTrackingService.Application.Common.Models;
using MediatR;
using Microsoft.Extensions.Logging;

namespace KartDeliveryTrackingService.Application.Features.IngestCarrierWebhook;

public sealed class IngestCarrierWebhookCommandHandler : IRequestHandler<IngestCarrierWebhookCommand, Result>
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private readonly ICarrierRegistry _carrierRegistry;
    private readonly ICarrierWebhookVerifier _verifier;
    private readonly IOutboxEventWriter _outboxEventWriter;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<IngestCarrierWebhookCommandHandler> _logger;

    public IngestCarrierWebhookCommandHandler(
        ICarrierRegistry carrierRegistry,
        ICarrierWebhookVerifier verifier,
        IOutboxEventWriter outboxEventWriter,
        TimeProvider timeProvider,
        ILogger<IngestCarrierWebhookCommandHandler> logger)
    {
        _carrierRegistry = carrierRegistry;
        _verifier = verifier;
        _outboxEventWriter = outboxEventWriter;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<Result> Handle(IngestCarrierWebhookCommand request, CancellationToken cancellationToken)
    {
        if (!_carrierRegistry.IsConfigured(request.CarrierId))
        {
            _logger.LogWarning("Stage {Stage}: rejected carrier webhook, {CarrierId} does not match a configured carrier adapter.", "CarrierNotConfigured", request.CarrierId);
            return Result.Failure(Error.Custom("carrier_not_configured", $"'{request.CarrierId}' does not match a configured carrier adapter."));
        }

        if (!_verifier.Verify(request.CarrierId, request.RawBody, request.SignatureHeader))
        {
            _logger.LogWarning("Stage {Stage}: rejected carrier webhook for {CarrierId}: signature missing or invalid.", "CarrierWebhookSignatureInvalid", request.CarrierId);
            return Result.Failure(Error.Custom("unauthorized_signature", "X-Carrier-Signature is missing or invalid."));
        }

        var rawPayload = Encoding.UTF8.GetString(request.RawBody);
        GenericCarrierWebhookBody? body;
        try
        {
            body = JsonSerializer.Deserialize<GenericCarrierWebhookBody>(request.RawBody, SerializerOptions);
        }
        catch (JsonException)
        {
            body = null;
        }

        var trackingId = body?.TrackingId;
        var statusCode = body?.StatusCode ?? body?.Status;
        if (string.IsNullOrWhiteSpace(trackingId) || string.IsNullOrWhiteSpace(statusCode))
        {
            _logger.LogWarning("Stage {Stage}: carrier webhook body for {CarrierId} missing required trackingId/statusCode.", "CarrierWebhookBodyValidationFailed", request.CarrierId);
            return Result.Failure(Error.Validation("Carrier webhook body must include trackingId and statusCode (or status)."));
        }

        var ingestion = new NormalizedCarrierIngestion(
            request.CarrierId,
            trackingId,
            statusCode,
            body!.EventTimestamp ?? _timeProvider.GetUtcNow(),
            rawPayload);

        try
        {
            await _outboxEventWriter.EnqueueAsync(
                "CarrierStatusIngested",
                ingestion.TrackingId,
                new CarrierStatusIngestedEventPayload(ingestion.CarrierId, ingestion.TrackingId, ingestion.CarrierStatusCode, ingestion.EventTimestamp, ingestion.RawPayload),
                deterministicId: null,
                cancellationToken);
        }
        catch (Exception ex)
        {
            // Durable enqueue failed (this service's Mongo outbox collection is unreachable) -
            // returned as a 5xx per requirement-spec.md S6 item 4(a), so the carrier's own
            // webhook-retry policy re-sends the call; this service has no broker-level
            // redelivery of its own for a webhook it never durably accepted.
            _logger.LogError(ex, "Stage {Stage}: failed to durably enqueue CarrierStatusIngested for {TrackingId} ({CarrierId}).", "CarrierStatusIngestEnqueueFailed", trackingId, request.CarrierId);
            return Result.Failure(Error.Custom("enqueue_failed", "Failed to durably accept this webhook."));
        }

        _logger.LogInformation(
            "Stage {Stage}: CarrierStatusIngested outbox event enqueued for {TrackingId} ({CarrierId})",
            "CarrierStatusIngestedOutboxEventEnqueued",
            trackingId,
            request.CarrierId);

        _logger.LogInformation(
            "Stage {Stage}: carrier webhook for {TrackingId} ({CarrierId}) accepted",
            "IngestCarrierWebhookAccepted",
            trackingId,
            request.CarrierId);

        return Result.Success();
    }
}
