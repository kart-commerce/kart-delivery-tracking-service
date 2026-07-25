namespace KartDeliveryTrackingService.Application.Common.Exceptions;

/// <summary>
/// A carrier status update arrived for a <c>trackingId</c> this service has not yet materialized a
/// <see cref="Domain.Tracking.TrackingRecord"/> for - the domain invariant that a tracking record's
/// identity must exist before any carrier status can be recorded against it
/// (requirement-spec.md S4) has not yet been satisfied, almost always because <c>ShipmentDispatched</c>
/// consumption (TRK-1) simply hasn't caught up yet. Thrown (rather than a Result failure) so the
/// message consumer's own retry ladder handles it as a transient condition - a genuine, not an
/// exceptional, outcome that resolves itself once TRK-1 catches up or the polling fallback (TRK-5)
/// re-establishes correctness.
/// </summary>
public sealed class TrackingRecordNotYetMaterializedException(string trackingId)
    : Exception($"No TrackingRecord exists yet for trackingId '{trackingId}'.");
