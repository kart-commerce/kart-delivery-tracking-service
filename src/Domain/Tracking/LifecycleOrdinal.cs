namespace KartDeliveryTrackingService.Domain.Tracking;

/// <summary>
/// database-design.md's "Ordinal Assignment" table - the position of a
/// <see cref="CanonicalDeliveryStatus"/> on the number line the non-regression guard compares
/// against. All three terminal values (<see cref="CanonicalDeliveryStatus.Delivered"/>,
/// <see cref="CanonicalDeliveryStatus.Returned"/>, <see cref="CanonicalDeliveryStatus.FailedDelivery"/>)
/// share the maximum ordinal, so whichever terminal value is recorded first wins and no later
/// terminal value (nor any other update) can overwrite it.
/// </summary>
public static class LifecycleOrdinal
{
    public static int For(CanonicalDeliveryStatus status) => status switch
    {
        CanonicalDeliveryStatus.Dispatched => 1,
        CanonicalDeliveryStatus.InTransit => 2,
        CanonicalDeliveryStatus.OutForDelivery => 3,
        CanonicalDeliveryStatus.Delivered => 4,
        CanonicalDeliveryStatus.Returned => 4,
        CanonicalDeliveryStatus.FailedDelivery => 4,
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, null),
    };

    public const int MaxOrdinal = 4;

    public static bool IsTerminal(CanonicalDeliveryStatus status) => For(status) == MaxOrdinal;
}
