using KartDeliveryTrackingService.Domain.Tracking;

namespace KartDeliveryTrackingService.Application.Common.Interfaces;

public sealed record TrackingRecordCreationResult(TrackingRecord Record, bool WasCreated);

public interface ITrackingRecordRepository
{
    Task<TrackingRecord?> GetAsync(string trackingId, CancellationToken cancellationToken);

    /// <summary>
    /// TRK-1: idempotent upsert keyed by <paramref name="trackingId"/> - a redelivered
    /// <c>ShipmentDispatched</c> message must not create a second record or regress an already
    /// further-advanced one (ddd-model.md's aggregate-creation invariant). Returns whether this
    /// call actually inserted the document, so the caller only writes the one-time SYSTEM history
    /// anchor (Modeling Decision 4) and sequence counter when it did.
    /// </summary>
    Task<TrackingRecordCreationResult> CreateIfNotExistsAsync(TrackingRecord record, CancellationToken cancellationToken);

    /// <summary>
    /// design-decisions.md "Concurrency Control for the Status-Regression Guard": an atomic,
    /// single-round-trip conditional update - applies only if <paramref name="incomingOrdinal"/>
    /// strictly exceeds the currently-stored ordinal. Returns <c>true</c> only if this call's
    /// update was the one applied.
    /// </summary>
    Task<bool> TryApplyOrdinalGuardedUpdateAsync(
        string trackingId,
        CanonicalDeliveryStatus newStatus,
        int incomingOrdinal,
        Eta newEta,
        DateTimeOffset now,
        string updatedBy,
        CancellationToken cancellationToken);

    /// <summary>
    /// TRK-5: database-design.md's partial index on <c>lastUpdatedAt</c> (non-terminal only) -
    /// every shipment stale past the polling threshold (edge-cases.md "Carrier Webhook Failure or
    /// Silence").
    /// </summary>
    Task<IReadOnlyList<TrackingRecord>> GetStaleNonTerminalAsync(DateTimeOffset staleBefore, int limit, CancellationToken cancellationToken);
}
