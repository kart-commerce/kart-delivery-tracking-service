namespace KartDeliveryTrackingService.Application.Common.Models;

/// <summary>event-contract.md Consumed Events: <c>ShipmentDispatched</c>, published by kart-shipping-service.</summary>
public sealed record ShipmentDispatchedEventPayload(string OrderId, string Carrier, string TrackingId);

/// <summary>
/// event-contract.md Published Events: <c>DeliveryStatusUpdated</c> - deliberately not expanded
/// beyond these two fields (event-contract.md's "Payload Resolution" section). <c>Status</c>
/// publishes the exact canonical enum name (e.g. the literal "Delivered" - ADR-0005).
/// </summary>
public sealed record DeliveryStatusUpdatedEventPayload(string TrackingId, string Status);

/// <summary>event-contract.md Internal-Only Events: <c>CarrierStatusIngested</c> - same shape as <see cref="NormalizedCarrierIngestion"/>.</summary>
public sealed record CarrierStatusIngestedEventPayload(
    string CarrierId,
    string TrackingId,
    string CarrierStatusCode,
    DateTimeOffset EventTimestamp,
    string RawPayload);

/// <summary>event-contract.md Internal-Only Events: <c>UnmappedCarrierStatusFlagged</c>.</summary>
public sealed record UnmappedCarrierStatusFlaggedEventPayload(
    string CarrierId,
    string TrackingId,
    string CarrierStatusCode,
    DateTimeOffset EventTimestamp);
