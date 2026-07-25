using FluentAssertions;
using KartDeliveryTrackingService.Domain.Tracking;
using Xunit;

namespace KartDeliveryTrackingService.UnitTests.Domain;

public class DedupKeyFactoryTests
{
    private static readonly DateTimeOffset EventTimestamp = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void FromContentHash_SameInputs_ProducesSameKey()
    {
        var first = DedupKeyFactory.FromContentHash("trk-1", "InTransit", EventTimestamp);
        var second = DedupKeyFactory.FromContentHash("trk-1", "InTransit", EventTimestamp);

        first.Should().Be(second);
    }

    [Theory]
    [InlineData("trk-2", "InTransit")]
    [InlineData("trk-1", "Delivered")]
    public void FromContentHash_DifferentInputs_ProducesDifferentKey(string trackingId, string status)
    {
        var baseline = DedupKeyFactory.FromContentHash("trk-1", "InTransit", EventTimestamp);
        var other = DedupKeyFactory.FromContentHash(trackingId, status, EventTimestamp);

        other.Should().NotBe(baseline);
    }

    [Fact]
    public void FromCarrierKey_PrefixesWithCarrierId_ToAvoidCrossCarrierCollisions()
    {
        DedupKeyFactory.FromCarrierKey("demo-carrier", "abc123").Should().Be("carrier:demo-carrier:abc123");
    }
}
