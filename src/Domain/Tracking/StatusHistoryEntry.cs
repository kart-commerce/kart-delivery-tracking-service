namespace KartDeliveryTrackingService.Domain.Tracking;

/// <summary>
/// ddd-model.md's <c>TrackingStatusHistory</c> aggregate's <c>StatusHistoryEntry</c> child entity -
/// identified by <c>(TrackingId, Sequence)</c>, append-only, never mutated or removed once
/// written. Backed by MongoDB's <c>tracking_status_history_entries</c> collection
/// (database-design.md). Records every distinct received state, including unmapped
/// (<see cref="CanonicalStatus"/> null) and regression-rejected updates, for audit.
/// </summary>
public sealed class StatusHistoryEntry
{
    public string TrackingId { get; }

    public long Sequence { get; }

    public string? CarrierStatusCode { get; }

    public CanonicalDeliveryStatus? CanonicalStatus { get; }

    public IngestionSource IngestionSource { get; }

    public string? RawPayload { get; }

    public DateTimeOffset EventTimestamp { get; }

    public DateTimeOffset ReceivedAt { get; }

    public string CreatedBy { get; }

    /// <summary>
    /// TRK-7: set once an untriaged unmapped entry (<see cref="CanonicalStatus"/> null, not a
    /// <see cref="IngestionSource.System"/> anchor) has been escalated past its shipment's ETA
    /// window (edge-cases.md "Unmapped Carrier Status"). Null until escalated; never reset.
    /// </summary>
    public DateTimeOffset? EscalatedAt { get; private set; }

    public StatusHistoryEntry(
        string trackingId,
        long sequence,
        string? carrierStatusCode,
        CanonicalDeliveryStatus? canonicalStatus,
        IngestionSource ingestionSource,
        string? rawPayload,
        DateTimeOffset eventTimestamp,
        DateTimeOffset receivedAt,
        string createdBy,
        DateTimeOffset? escalatedAt = null)
    {
        TrackingId = trackingId;
        Sequence = sequence;
        CarrierStatusCode = carrierStatusCode;
        CanonicalStatus = canonicalStatus;
        IngestionSource = ingestionSource;
        RawPayload = rawPayload;
        EventTimestamp = eventTimestamp;
        ReceivedAt = receivedAt;
        CreatedBy = createdBy;
        EscalatedAt = escalatedAt;
    }

    public bool IsUntriagedUnmapped => CanonicalStatus is null && IngestionSource != IngestionSource.System && EscalatedAt is null;

    public void MarkEscalated(DateTimeOffset now) => EscalatedAt = now;
}
