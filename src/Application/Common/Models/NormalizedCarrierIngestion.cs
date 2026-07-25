namespace KartDeliveryTrackingService.Application.Common.Models;

/// <summary>
/// api-contract.yaml's <c>NormalizedCarrierIngestion</c> schema - the fixed internal shape every
/// per-carrier adapter normalizes a carrier's native webhook body into (requirement-spec.md S5).
/// This is the payload of the internal <c>CarrierStatusIngested</c> event.
/// </summary>
public sealed record NormalizedCarrierIngestion(
    string CarrierId,
    string TrackingId,
    string CarrierStatusCode,
    DateTimeOffset EventTimestamp,
    string RawPayload);
