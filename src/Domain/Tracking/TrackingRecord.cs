using Kart.Shared.Domain;

namespace KartDeliveryTrackingService.Domain.Tracking;

/// <summary>
/// ddd-model.md's <c>TrackingRecord</c> aggregate - one document per <see cref="TrackingId"/>,
/// holding only the *current* exposed state. Backed by MongoDB's <c>tracking_records</c>
/// collection (database-design.md); this type models the aggregate's invariants, while the actual
/// atomic conditional update (the non-regression guard) is enforced by a Mongo
/// <c>findOneAndUpdate</c> filter at the repository layer (design-decisions.md's "Concurrency
/// Control for the Status-Regression Guard") using the same ordinal comparison
/// <see cref="TryApplyStatusUpdate"/> models here for validation/tests.
/// </summary>
public sealed class TrackingRecord
{
    public string TrackingId { get; private set; } = string.Empty;

    public string OrderId { get; private set; } = string.Empty;

    public string CarrierId { get; private set; } = string.Empty;

    public CanonicalDeliveryStatus CurrentStatus { get; private set; }

    public int CurrentOrdinal { get; private set; }

    public Eta Eta { get; private set; } = null!;

    public DateTimeOffset LastUpdatedAt { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public string CreatedBy { get; private set; } = string.Empty;

    public string UpdatedBy { get; private set; } = string.Empty;

    private TrackingRecord()
    {
    }

    /// <summary>
    /// TRK-1: creates the addressable tracking record on <c>ShipmentDispatched</c> consumption
    /// (ddd-model.md's aggregate-creation invariant). The initial status is
    /// <see cref="CanonicalDeliveryStatus.Dispatched"/> - the semantic state a just-dispatched
    /// shipment is in - with its ETA computed from the carrier's static SLA fallback table, since
    /// <c>ShipmentDispatched</c>'s payload carries no carrier-supplied ETA of its own.
    /// </summary>
    public static Result<TrackingRecord> Create(
        string trackingId,
        string orderId,
        string carrierId,
        Eta eta,
        DateTimeOffset now,
        string createdBy)
    {
        if (string.IsNullOrWhiteSpace(trackingId))
        {
            return Result.Failure<TrackingRecord>(Error.Validation("trackingId is required."));
        }

        if (string.IsNullOrWhiteSpace(orderId))
        {
            return Result.Failure<TrackingRecord>(Error.Validation("orderId is required."));
        }

        if (string.IsNullOrWhiteSpace(carrierId))
        {
            return Result.Failure<TrackingRecord>(Error.Validation("carrierId is required."));
        }

        var record = new TrackingRecord
        {
            TrackingId = trackingId,
            OrderId = orderId,
            CarrierId = carrierId,
            CurrentStatus = CanonicalDeliveryStatus.Dispatched,
            CurrentOrdinal = LifecycleOrdinal.For(CanonicalDeliveryStatus.Dispatched),
            Eta = eta,
            LastUpdatedAt = now,
            CreatedAt = now,
            CreatedBy = createdBy,
            UpdatedBy = createdBy,
        };

        return Result.Success(record);
    }

    /// <summary>
    /// Rehydrates a <see cref="TrackingRecord"/> already persisted in MongoDB. Skips every
    /// invariant check <see cref="Create"/> runs - those only apply at construction time.
    /// </summary>
    public static TrackingRecord Rehydrate(
        string trackingId,
        string orderId,
        string carrierId,
        CanonicalDeliveryStatus currentStatus,
        int currentOrdinal,
        Eta eta,
        DateTimeOffset lastUpdatedAt,
        DateTimeOffset createdAt,
        string createdBy,
        string updatedBy) => new()
        {
            TrackingId = trackingId,
            OrderId = orderId,
            CarrierId = carrierId,
            CurrentStatus = currentStatus,
            CurrentOrdinal = currentOrdinal,
            Eta = eta,
            LastUpdatedAt = lastUpdatedAt,
            CreatedAt = createdAt,
            CreatedBy = createdBy,
            UpdatedBy = updatedBy,
        };

    /// <summary>
    /// edge-cases.md "Out-of-Order Carrier Status Updates": rejects (returns
    /// <c>Result.Success(false)</c>, never a failure - a regression is an expected, not an
    /// exceptional, outcome) any update whose ordinal does not strictly exceed
    /// <see cref="CurrentOrdinal"/>. The repository performs the equivalent atomic check directly
    /// against MongoDB (<c>currentOrdinal: {'$lt': incomingOrdinal}</c>) so this in-memory
    /// application only needs to agree with, never substitute for, that database-level guard.
    /// </summary>
    public bool TryApplyStatusUpdate(CanonicalDeliveryStatus newStatus, Eta newEta, DateTimeOffset now, string updatedBy)
    {
        var incomingOrdinal = LifecycleOrdinal.For(newStatus);
        if (incomingOrdinal <= CurrentOrdinal)
        {
            return false;
        }

        CurrentStatus = newStatus;
        CurrentOrdinal = incomingOrdinal;
        Eta = newEta;
        LastUpdatedAt = now;
        UpdatedBy = updatedBy;
        return true;
    }

    public bool IsTerminal => LifecycleOrdinal.IsTerminal(CurrentStatus);
}
