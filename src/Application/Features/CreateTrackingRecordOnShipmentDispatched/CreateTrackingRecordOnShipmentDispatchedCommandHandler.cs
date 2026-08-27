using Kart.Shared.Domain;
using KartDeliveryTrackingService.Application.Common;
using KartDeliveryTrackingService.Application.Common.Interfaces;
using KartDeliveryTrackingService.Domain.Tracking;
using MediatR;
using Microsoft.Extensions.Logging;

namespace KartDeliveryTrackingService.Application.Features.CreateTrackingRecordOnShipmentDispatched;

public sealed class CreateTrackingRecordOnShipmentDispatchedCommandHandler
    : IRequestHandler<CreateTrackingRecordOnShipmentDispatchedCommand, Result>
{
    private readonly ITrackingRecordRepository _trackingRecords;
    private readonly ITrackingStatusHistoryRepository _statusHistory;
    private readonly ICarrierRegistry _carrierRegistry;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<CreateTrackingRecordOnShipmentDispatchedCommandHandler> _logger;

    public CreateTrackingRecordOnShipmentDispatchedCommandHandler(
        ITrackingRecordRepository trackingRecords,
        ITrackingStatusHistoryRepository statusHistory,
        ICarrierRegistry carrierRegistry,
        TimeProvider timeProvider,
        ILogger<CreateTrackingRecordOnShipmentDispatchedCommandHandler> logger)
    {
        _trackingRecords = trackingRecords;
        _statusHistory = statusHistory;
        _carrierRegistry = carrierRegistry;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<Result> Handle(CreateTrackingRecordOnShipmentDispatchedCommand request, CancellationToken cancellationToken)
    {
        var now = _timeProvider.GetUtcNow();
        var eta = new Eta(_carrierRegistry.ComputeSlaFallbackEta(request.Carrier, now), EtaSource.SlaFallback);

        var creation = TrackingRecord.Create(
            request.TrackingId,
            request.OrderId,
            request.Carrier,
            eta,
            now,
            SystemPrincipals.ShipmentDispatchedConsumer);

        if (creation.IsFailure)
        {
            return Result.Failure(creation.Error);
        }

        var outcome = await _trackingRecords.CreateIfNotExistsAsync(creation.Value, cancellationToken);

        if (!outcome.WasCreated)
        {
            // Idempotent redelivery of the same ShipmentDispatched message (or a message that
            // arrived after this trackingId was already materialized some other way) - the
            // Domain Invariant requiring idempotent consumption is satisfied by simply not
            // duplicating work, not by treating this as an error.
            _logger.LogInformation(
                "Stage {Stage}: ShipmentDispatched for {TrackingId} already materialized; skipping duplicate creation.",
                "ShipmentDispatchedIdempotentReplaySkipped",
                request.TrackingId);
            return Result.Success();
        }

        var sequence = await _statusHistory.NextSequenceAsync(request.TrackingId, cancellationToken);
        var anchorEntry = new StatusHistoryEntry(
            request.TrackingId,
            sequence,
            carrierStatusCode: null,
            canonicalStatus: CanonicalDeliveryStatus.Dispatched,
            IngestionSource.System,
            rawPayload: null,
            eventTimestamp: now,
            receivedAt: now,
            SystemPrincipals.ShipmentDispatchedConsumer);

        // Modeling Decision 4: a single SYSTEM-sourced anchor entry, deterministically keyed so a
        // retry of this same handler call (e.g. the caller re-checks CreateIfNotExistsAsync's
        // result under a race) never produces a second anchor.
        await _statusHistory.AppendAsync($"{request.TrackingId}:system-anchor", anchorEntry, cancellationToken);

        _logger.LogInformation(
            "Stage {Stage}: tracking record persisted for {TrackingId} (order {OrderId}, carrier {Carrier}), system anchor status-history entry appended.",
            "TrackingRecordPersisted",
            request.TrackingId,
            request.OrderId,
            request.Carrier);

        return Result.Success();
    }
}
