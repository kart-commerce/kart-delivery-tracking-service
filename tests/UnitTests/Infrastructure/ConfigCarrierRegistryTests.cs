using FluentAssertions;
using KartDeliveryTrackingService.Application.Common.Options;
using KartDeliveryTrackingService.Domain.Tracking;
using KartDeliveryTrackingService.Infrastructure.Carriers;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace KartDeliveryTrackingService.UnitTests.Infrastructure;

public class ConfigCarrierRegistryTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static ConfigCarrierRegistry CreateRegistry(CarrierRegistryOptions options) =>
        new(Options.Create(options), NullLogger<ConfigCarrierRegistry>.Instance);

    private static CarrierRegistryOptions DemoCarrierOptions() => new()
    {
        ["demo-carrier"] = new CarrierConfig
        {
            WebhookSharedSecret = "shh",
            StatusMap = new Dictionary<string, string> { ["PICKED_UP"] = "Dispatched", ["DELIVERED"] = "Delivered" },
            SlaFallbackDays = 5,
        },
    };

    [Fact]
    public void MapStatus_WithKnownCode_ReturnsCanonicalStatus()
    {
        var registry = CreateRegistry(DemoCarrierOptions());

        registry.MapStatus("demo-carrier", "DELIVERED").Should().Be(CanonicalDeliveryStatus.Delivered);
    }

    [Fact]
    public void MapStatus_WithUnknownCode_ReturnsNull_NotASyntheticUnknownValue()
    {
        var registry = CreateRegistry(DemoCarrierOptions());

        registry.MapStatus("demo-carrier", "SOMETHING_NEW").Should().BeNull();
    }

    [Fact]
    public void MapStatus_ForUnconfiguredCarrier_ReturnsNull()
    {
        var registry = CreateRegistry(DemoCarrierOptions());

        registry.MapStatus("other-carrier", "DELIVERED").Should().BeNull();
    }

    [Fact]
    public void ComputeSlaFallbackEta_UsesConfiguredDays()
    {
        var registry = CreateRegistry(DemoCarrierOptions());

        registry.ComputeSlaFallbackEta("demo-carrier", Now).Should().Be(Now.AddDays(5));
    }

    [Fact]
    public void ComputeSlaFallbackEta_ForUnconfiguredCarrier_FallsBackToPlatformDefault_NeverThrows()
    {
        var registry = CreateRegistry(DemoCarrierOptions());

        registry.ComputeSlaFallbackEta("unknown-carrier", Now).Should().Be(Now.AddDays(5));
    }

    [Fact]
    public void TryGetWebhookSharedSecret_ForConfiguredCarrier_ReturnsTrueAndSecret()
    {
        var registry = CreateRegistry(DemoCarrierOptions());

        registry.TryGetWebhookSharedSecret("demo-carrier", out var secret).Should().BeTrue();
        secret.Should().Be("shh");
    }

    [Fact]
    public void TryGetWebhookSharedSecret_ForUnconfiguredCarrier_ReturnsFalse()
    {
        var registry = CreateRegistry(DemoCarrierOptions());

        registry.TryGetWebhookSharedSecret("unknown-carrier", out _).Should().BeFalse();
    }
}
