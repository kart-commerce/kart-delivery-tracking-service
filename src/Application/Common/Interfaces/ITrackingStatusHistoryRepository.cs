using KartDeliveryTrackingService.Domain.Tracking;

namespace KartDeliveryTrackingService.Application.Common.Interfaces;

public interface ITrackingStatusHistoryRepository
{
    /// <summary>
    /// Allocates the next monotonic <c>sequence</c> value for <paramref name="trackingId"/> via
    /// the dedicated <c>tracking_status_history_counters</c> atomic-counter collection
    /// (database-design.md "Sequence Assignment") - a single-document upsert-and-increment, the
    /// same atomicity pattern as the ordinal guard, deliberately uncoupled from
    /// <c>tracking_records</c> itself.
    /// </summary>
    Task<long> NextSequenceAsync(string trackingId, CancellationToken cancellationToken);

    /// <summary>
    /// Appends one entry. Idempotent via <paramref name="deterministicId"/> (an upsert, not a
    /// plain insert) so a redelivered message that reaches this step twice before its dedup entry
    /// is finalized (see <see cref="IWebhookDedupRepository"/>) never produces two rows for the
    /// same logical event.
    /// </summary>
    Task AppendAsync(string deterministicId, StatusHistoryEntry entry, CancellationToken cancellationToken);

    /// <summary>
    /// TRK-7: database-design.md's partial index (<c>canonicalStatus: null</c>, non-<c>SYSTEM</c>)
    /// - untriaged unmapped entries, not yet cross-referenced against their shipment's ETA. The
    /// handler joins each against <see cref="ITrackingRecordRepository"/> to test the ETA-window
    /// escalation trigger (edge-cases.md "Unmapped Carrier Status") - a full aggregation-pipeline
    /// join is unwarranted given unmapped entries are expected to be a small fraction of traffic.
    /// </summary>
    Task<IReadOnlyList<StatusHistoryEntry>> GetUntriagedUnmappedAsync(int limit, CancellationToken cancellationToken);

    Task MarkEscalatedAsync(string trackingId, long sequence, DateTimeOffset now, CancellationToken cancellationToken);
}
