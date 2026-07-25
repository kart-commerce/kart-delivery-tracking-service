namespace KartDeliveryTrackingService.Application.Common.Options;

/// <summary>Binds one entry of the "Carriers" configuration section.</summary>
public sealed class CarrierConfig
{
    public string WebhookSharedSecret { get; set; } = string.Empty;

    /// <summary>Raw carrier status code -> <see cref="Domain.Tracking.CanonicalDeliveryStatus"/> name.</summary>
    public Dictionary<string, string> StatusMap { get; set; } = new();

    public int SlaFallbackDays { get; set; } = 5;

    public string? PollingEndpointTemplate { get; set; }
}

/// <summary>
/// Binds the entire "Carriers" configuration section - keyed by carrier id
/// (<c>POST /internal/v1/webhooks/carriers/{carrierId}</c>'s path parameter).
/// </summary>
public sealed class CarrierRegistryOptions : Dictionary<string, CarrierConfig>
{
}
