namespace KartDeliveryTrackingService.UnitTests;

/// <summary>Fixed-clock TimeProvider for deterministic handler tests.</summary>
public sealed class FakeTimeProvider : TimeProvider
{
    private readonly DateTimeOffset _now;

    public FakeTimeProvider(DateTimeOffset now)
    {
        _now = now;
    }

    public override DateTimeOffset GetUtcNow() => _now;
}
