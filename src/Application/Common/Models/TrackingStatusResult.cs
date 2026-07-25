using KartDeliveryTrackingService.Domain.Tracking;

namespace KartDeliveryTrackingService.Application.Common.Models;

/// <summary>api-contract.yaml's <c>TrackingResponse</c> schema - the 200 branch.</summary>
public sealed record TrackingResponse(string TrackingId, CanonicalDeliveryStatus Status, EtaResponse Eta, DateTimeOffset LastUpdatedAt);

/// <summary>
/// api-contract.yaml's <c>PendingTrackingResponse</c> schema - the 202 branch. <see cref="Status"/>
/// is always the literal "PENDING" - a required field on the wire, but not a settable member,
/// since it is never anything else (ddd-model.md Modeling Decision 2: "PENDING" is an API-layer-
/// only marker, never a member of <see cref="CanonicalDeliveryStatus"/>).
/// </summary>
public sealed record PendingTrackingResponse(string TrackingId, string Message)
{
    public string Status => "PENDING";
}

/// <summary>
/// TRK-2: <c>GetTrackingStatus</c> never fails in the Result sense - api-contract.yaml deliberately
/// never returns 404 (ddd-model.md Modeling Decision 2). Exactly one of the two members is set;
/// the controller maps <see cref="Tracking"/> to 200 and <see cref="Pending"/> to 202.
/// </summary>
public sealed record TrackingStatusResult(TrackingResponse? Tracking, PendingTrackingResponse? Pending)
{
    public static TrackingStatusResult Found(TrackingResponse response) => new(response, null);

    public static TrackingStatusResult AsPending(string trackingId) => new(
        null,
        new PendingTrackingResponse(trackingId, "Tracking details for this shipment are not yet available. Please check back shortly."));
}
