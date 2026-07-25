namespace KartDeliveryTrackingService.Domain.Tracking;

/// <summary>
/// ddd-model.md's <see cref="TrackingStatusHistory"/> value object. <see cref="System"/> marks the
/// single synthetic anchor entry written at <see cref="TrackingRecord"/> creation (Modeling
/// Decision 4); <see cref="Webhook"/>/<see cref="Poll"/> mark entries from the two carrier-status
/// ingestion paths.
/// </summary>
public enum IngestionSource
{
    System,
    Webhook,
    Poll,
}
