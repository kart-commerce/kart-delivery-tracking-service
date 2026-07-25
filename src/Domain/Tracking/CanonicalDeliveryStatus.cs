namespace KartDeliveryTrackingService.Domain.Tracking;

/// <summary>
/// Single internal status vocabulary every exposed/published status must belong to (ddd-model.md;
/// edge-cases.md "Inconsistent Status Vocabulary Across Carriers"). Never a raw carrier-native
/// code, never <c>PENDING</c>/<c>Unknown</c> - those are API-layer-only conditions, not members of
/// this enum. <see cref="Delivered"/> is the one non-negotiable literal: Order's consumption of
/// <c>DeliveryStatusUpdated</c> (ADR-0005) filters on the exact wire string "Delivered".
/// </summary>
public enum CanonicalDeliveryStatus
{
    Dispatched,
    InTransit,
    OutForDelivery,
    Delivered,
    Returned,
    FailedDelivery,
}
