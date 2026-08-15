using Kart.Shared.Domain;
using KartDeliveryTrackingService.Application.Common;
using KartDeliveryTrackingService.Application.Common.Exceptions;
using KartDeliveryTrackingService.Application.Common.Interfaces;
using KartDeliveryTrackingService.Application.Common.Models;
using KartDeliveryTrackingService.Application.Common.Options;
using KartDeliveryTrackingService.Domain.Tracking;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace KartDeliveryTrackingService.Application.Features.ApplyCarrierStatusUpdate;

public sealed class ApplyCarrierStatusUpdateCommandHandler : IRequestHandler<ApplyCarrierStatusUpdateCommand, Result>
{
    private readonly ITrackingRecordRepository _trackingRecords;
    private readonly ITrackingStatusHistoryRepository _statusHistory;
    private readonly IWebhookDedupRepository _dedup;
    private readonly IOutboxEventWriter _outboxEventWriter;
    private readonly ICarrierRegistry _carrierRegistry;
    private readonly TrackingOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<ApplyCarrierStatusUpdateCommandHandler> _logger;

    public ApplyCarrierStatusUpdateCommandHandler(
        ITrackingRecordRepository trackingRecords,
        ITrackingStatusHistoryRepository statusHistory,
        IWebhookDedupRepository dedup,
        IOutboxEventWriter outboxEventWriter,
        ICarrierRegistry carrierRegistry,
        IOptions<TrackingOptions> options,
        TimeProvider timeProvider,
        ILogger<ApplyCarrierStatusUpdateCommandHandler> logger)
    {
        _trackingRecords = trackingRecords;
        _statusHistory = statusHistory;
        _dedup = dedup;
        _outboxEventWriter = outboxEventWriter;
        _carrierRegistry = carrierRegistry;
        _options = options.Value;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<Result> Handle(ApplyCarrierStatusUpdateCommand request, CancellationToken cancellationToken)
    {
        var now = _timeProvider.GetUtcNow();
        var canonicalStatus = _carrierRegistry.MapStatus(request.CarrierId, request.CarrierStatusCode);
        var statusOrRawCode = canonicalStatus?.ToString() ?? request.CarrierStatusCode;
        var dedupKey = DedupKeyFactory.FromContentHash(request.TrackingId, statusOrRawCode, request.EventTimestamp);

        // Step 1 (read-only): a hit means this exact update was already fully processed - see
        // IWebhookDedupRepository.InsertAsync's remarks for why the insert only happens at the
        // very end of this pipeline, which is what makes this check-then-fully-reprocess sequence
        // safe to re-run end to end on message redelivery.
        var existingDedupEntry = await _dedup.TryGetAsync(dedupKey, cancellationToken);
        if (existingDedupEntry is not null)
        {
            _logger.LogInformation(
                "Stage {Stage}: suppressed duplicate carrier status update for {TrackingId} (dedup key already recorded).",
                "DuplicateCarrierStatusUpdateSuppressed",
                request.TrackingId);
            return Result.Success();
        }

        var record = await _trackingRecords.GetAsync(request.TrackingId, cancellationToken)
            ?? throw new TrackingRecordNotYetMaterializedException(request.TrackingId);

        var updatedBy = request.IngestionSource == IngestionSource.Poll
            ? SystemPrincipals.CarrierPoll(request.CarrierId)
            : SystemPrincipals.CarrierWebhook(request.CarrierId);

        var receivedAt = now;
        var applied = false;

        var sequence = await _statusHistory.NextSequenceAsync(request.TrackingId, cancellationToken);

        if (canonicalStatus is null)
        {
            // edge-cases.md "Unmapped Carrier Status": persisted for audit/triage, but never
            // becomes the exposed current status and never publishes DeliveryStatusUpdated.
            await _statusHistory.AppendAsync(
                dedupKey,
                new StatusHistoryEntry(request.TrackingId, sequence, request.CarrierStatusCode, null, request.IngestionSource, request.RawPayload, request.EventTimestamp, receivedAt, updatedBy),
                cancellationToken);

            await _outboxEventWriter.EnqueueAsync(
                "UnmappedCarrierStatusFlagged",
                request.TrackingId,
                new UnmappedCarrierStatusFlaggedEventPayload(request.CarrierId, request.TrackingId, request.CarrierStatusCode, request.EventTimestamp),
                deterministicId: $"{dedupKey}:unmapped-carrier-status-flagged",
                cancellationToken);

            _logger.LogWarning(
                "Stage {Stage}: unmapped carrier status code {CarrierStatusCode} for carrier {CarrierId}, tracking {TrackingId} - status history persisted, outbox event enqueued, flagged for triage.",
                "UnmappedCarrierStatusFlagged",
                request.CarrierStatusCode,
                request.CarrierId,
                request.TrackingId);
        }
        else
        {
            var incomingOrdinal = LifecycleOrdinal.For(canonicalStatus.Value);

            // api-contract.yaml's NormalizedCarrierIngestion carries no carrier-supplied ETA field
            // today (only carrierId/trackingId/carrierStatusCode/eventTimestamp/rawPayload) even
            // though requirement-spec.md's resolved ETA decision describes passing one through
            // when a carrier supplies it. Until a real carrier integration extends that contract
            // with an ETA field, EtaSource.CarrierSupplied is unreachable in practice - every
            // update recomputes the static SLA fallback, honestly reflecting today's actual
            // ingestion contract rather than silently assuming a field that doesn't exist yet.
            var eta = new Eta(_carrierRegistry.ComputeSlaFallbackEta(request.CarrierId, now), EtaSource.SlaFallback);

            applied = await _trackingRecords.TryApplyOrdinalGuardedUpdateAsync(
                request.TrackingId, canonicalStatus.Value, incomingOrdinal, eta, now, updatedBy, cancellationToken);

            await _statusHistory.AppendAsync(
                dedupKey,
                new StatusHistoryEntry(request.TrackingId, sequence, request.CarrierStatusCode, canonicalStatus, request.IngestionSource, request.RawPayload, request.EventTimestamp, receivedAt, updatedBy),
                cancellationToken);

            if (applied)
            {
                await _outboxEventWriter.EnqueueAsync(
                    "DeliveryStatusUpdated",
                    request.TrackingId,
                    new DeliveryStatusUpdatedEventPayload(request.TrackingId, canonicalStatus.Value.ToString()),
                    deterministicId: $"{dedupKey}:delivery-status-updated",
                    cancellationToken);

                _logger.LogInformation(
                    "Stage {Stage}: carrier status transition to {Status} accepted for {TrackingId}, status history persisted, DeliveryStatusUpdated outbox event enqueued.",
                    "CarrierStatusTransitionAcceptedOutboxEventEnqueued",
                    canonicalStatus,
                    request.TrackingId);
            }
            else
            {
                _logger.LogInformation(
                    "Stage {Stage}: rejected regressing/duplicate-ordinal carrier status {Status} for {TrackingId} (current ordinal not exceeded).",
                    "CarrierStatusTransitionRejectedOutOfOrder",
                    canonicalStatus,
                    request.TrackingId);
            }
        }

        // Last step: marks this exact update as fully processed (see remarks on the dedup check
        // above).
        await _dedup.InsertAsync(
            new WebhookDedupEntry(dedupKey, request.TrackingId, now, updatedBy, now.AddDays(_options.DedupEntryTtlDays)),
            cancellationToken);

        if (applied && canonicalStatus is not null && LifecycleOrdinal.IsTerminal(canonicalStatus.Value))
        {
            // TRK-6: retarget every still-live dedup entry for this trackingId to
            // terminal + 30-day grace (edge-cases.md "Duplicate Carrier Webhook Delivery").
            await _dedup.RetargetExpiryForTrackingIdAsync(
                request.TrackingId,
                now.AddDays(_options.DedupEntryTerminalGraceDays),
                now,
                SystemPrincipals.TerminalStatusSweep,
                cancellationToken);

            _logger.LogInformation(
                "Stage {Stage}: {TrackingId} reached terminal status {Status}; dedup entries retargeted to terminal grace window.",
                "TerminalDeliveryStatusDedupRetargeted",
                request.TrackingId,
                canonicalStatus);
        }

        return Result.Success();
    }
}
