using Kart.Shared.Domain;
using KartDeliveryTrackingService.Application.Common.Interfaces;
using MediatR;
using Microsoft.Extensions.Logging;

namespace KartDeliveryTrackingService.Application.Features.EscalateUnmappedStatusAlerts;

public sealed class EscalateUnmappedStatusAlertsCommandHandler : IRequestHandler<EscalateUnmappedStatusAlertsCommand, Result<int>>
{
    private const int BatchSize = 200;

    private readonly ITrackingStatusHistoryRepository _statusHistory;
    private readonly ITrackingRecordRepository _trackingRecords;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<EscalateUnmappedStatusAlertsCommandHandler> _logger;

    public EscalateUnmappedStatusAlertsCommandHandler(
        ITrackingStatusHistoryRepository statusHistory,
        ITrackingRecordRepository trackingRecords,
        TimeProvider timeProvider,
        ILogger<EscalateUnmappedStatusAlertsCommandHandler> logger)
    {
        _statusHistory = statusHistory;
        _trackingRecords = trackingRecords;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<Result<int>> Handle(EscalateUnmappedStatusAlertsCommand request, CancellationToken cancellationToken)
    {
        var now = _timeProvider.GetUtcNow();
        var candidates = await _statusHistory.GetUntriagedUnmappedAsync(BatchSize, cancellationToken);
        var escalated = 0;

        foreach (var entry in candidates)
        {
            var record = await _trackingRecords.GetAsync(entry.TrackingId, cancellationToken);
            if (record is null || record.Eta.Value > now)
            {
                continue;
            }

            // Critical, not Warning: this is the same-day-page tier (edge-cases.md) - the
            // shipment's promised delivery window has already passed while its status is still
            // unresolved, which is customer-visible risk, not routine triage noise.
            _logger.LogCritical(
                "Unmapped carrier status for {TrackingId} (carrier {CarrierId}, code {CarrierStatusCode}) is still untriaged past its ETA window ({Eta}) - escalating.",
                entry.TrackingId,
                record.CarrierId,
                entry.CarrierStatusCode,
                record.Eta.Value);

            await _statusHistory.MarkEscalatedAsync(entry.TrackingId, entry.Sequence, now, cancellationToken);
            escalated++;
        }

        return Result.Success(escalated);
    }
}
