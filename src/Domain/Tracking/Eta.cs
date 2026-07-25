namespace KartDeliveryTrackingService.Domain.Tracking;

/// <summary>
/// ddd-model.md's <c>Eta</c> value object - recomputed on every accepted status update, never on
/// an independent schedule (edge-cases.md "ETA Going Stale Mid-Transit").
/// </summary>
public sealed record Eta(DateTimeOffset Value, EtaSource Source);
