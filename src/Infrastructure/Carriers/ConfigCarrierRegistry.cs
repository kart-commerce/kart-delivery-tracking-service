using KartDeliveryTrackingService.Application.Common.Interfaces;
using KartDeliveryTrackingService.Application.Common.Options;
using KartDeliveryTrackingService.Domain.Tracking;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace KartDeliveryTrackingService.Infrastructure.Carriers;

/// <summary>
/// ddd-model.md Modeling Decision 5's reference-data lookup, backed by the "Carriers"
/// configuration section (<see cref="CarrierRegistryOptions"/>) - the generic adapter surface
/// every configured carrier is read through.
/// </summary>
public sealed class ConfigCarrierRegistry : ICarrierRegistry
{
    private const int DefaultSlaFallbackDays = 5;

    private readonly CarrierRegistryOptions _carriers;
    private readonly ILogger<ConfigCarrierRegistry> _logger;

    public ConfigCarrierRegistry(IOptions<CarrierRegistryOptions> options, ILogger<ConfigCarrierRegistry> logger)
    {
        _carriers = options.Value;
        _logger = logger;
    }

    public bool IsConfigured(string carrierId) => _carriers.ContainsKey(carrierId);

    public bool TryGetWebhookSharedSecret(string carrierId, out string secret)
    {
        if (_carriers.TryGetValue(carrierId, out var config) && !string.IsNullOrEmpty(config.WebhookSharedSecret))
        {
            secret = config.WebhookSharedSecret;
            return true;
        }

        secret = string.Empty;
        return false;
    }

    public CanonicalDeliveryStatus? MapStatus(string carrierId, string carrierStatusCode)
    {
        if (!_carriers.TryGetValue(carrierId, out var config))
        {
            return null;
        }

        if (config.StatusMap.TryGetValue(carrierStatusCode, out var mapped) &&
            Enum.TryParse<CanonicalDeliveryStatus>(mapped, ignoreCase: true, out var status))
        {
            return status;
        }

        return null;
    }

    public DateTimeOffset ComputeSlaFallbackEta(string carrierId, DateTimeOffset dispatchedAt)
    {
        if (_carriers.TryGetValue(carrierId, out var config))
        {
            return dispatchedAt.AddDays(config.SlaFallbackDays);
        }

        // requirement-spec.md's ETA decision assumes a maintained per-carrier SLA table; a
        // carrier referenced by ShipmentDispatched but missing from configuration must still get
        // a usable ETA rather than block TrackingRecord creation - falls back to a generic
        // platform default and logs, rather than throwing.
        _logger.LogWarning("No SLA fallback configured for carrier {CarrierId}; using the platform default of {Days} days.", carrierId, DefaultSlaFallbackDays);
        return dispatchedAt.AddDays(DefaultSlaFallbackDays);
    }

    public string? PollingEndpointTemplate(string carrierId) =>
        _carriers.TryGetValue(carrierId, out var config) ? config.PollingEndpointTemplate : null;
}
