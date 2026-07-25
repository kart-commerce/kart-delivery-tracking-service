using FluentAssertions;
using KartDeliveryTrackingService.Domain.Tracking;
using Xunit;

namespace KartDeliveryTrackingService.UnitTests.Domain;

public class LifecycleOrdinalTests
{
    [Theory]
    [InlineData(CanonicalDeliveryStatus.Dispatched, 1)]
    [InlineData(CanonicalDeliveryStatus.InTransit, 2)]
    [InlineData(CanonicalDeliveryStatus.OutForDelivery, 3)]
    [InlineData(CanonicalDeliveryStatus.Delivered, 4)]
    [InlineData(CanonicalDeliveryStatus.Returned, 4)]
    [InlineData(CanonicalDeliveryStatus.FailedDelivery, 4)]
    public void For_ReturnsExpectedOrdinal(CanonicalDeliveryStatus status, int expectedOrdinal)
    {
        LifecycleOrdinal.For(status).Should().Be(expectedOrdinal);
    }

    [Theory]
    [InlineData(CanonicalDeliveryStatus.Delivered)]
    [InlineData(CanonicalDeliveryStatus.Returned)]
    [InlineData(CanonicalDeliveryStatus.FailedDelivery)]
    public void IsTerminal_TrueForAllThreeTerminalAlternates(CanonicalDeliveryStatus status)
    {
        LifecycleOrdinal.IsTerminal(status).Should().BeTrue();
    }

    [Theory]
    [InlineData(CanonicalDeliveryStatus.Dispatched)]
    [InlineData(CanonicalDeliveryStatus.InTransit)]
    [InlineData(CanonicalDeliveryStatus.OutForDelivery)]
    public void IsTerminal_FalseForProgressingStatuses(CanonicalDeliveryStatus status)
    {
        LifecycleOrdinal.IsTerminal(status).Should().BeFalse();
    }
}
