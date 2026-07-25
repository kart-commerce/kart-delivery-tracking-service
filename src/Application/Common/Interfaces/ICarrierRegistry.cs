using KartDeliveryTrackingService.Domain.Tracking;

namespace KartDeliveryTrackingService.Application.Common.Interfaces;

/// <summary>
/// ddd-model.md Modeling Decision 5: the per-carrier status-mapping and SLA-ETA tables are
/// maintained, out-of-band, static reference/configuration data, not aggregates. This is the
/// generic adapter/lookup surface every integrated carrier is read through - adding a carrier is a
/// configuration change (see appsettings.json's <c>Carriers</c> section), never a new C# type,
/// since no two real per-carrier integrations exist yet to justify one bespoke class each.
/// </summary>
public interface ICarrierRegistry
{
    bool IsConfigured(string carrierId);

    /// <summary>Per-carrier shared secret for <c>X-Carrier-Signature</c> HMAC-SHA256 verification.</summary>
    bool TryGetWebhookSharedSecret(string carrierId, out string secret);

    /// <summary>
    /// edge-cases.md "Inconsistent Status Vocabulary Across Carriers": maps a carrier's raw status
    /// code to the canonical enum. Null means unmapped (edge-cases.md "Unmapped Carrier Status") -
    /// never a synthetic "unknown" enum member.
    /// </summary>
    CanonicalDeliveryStatus? MapStatus(string carrierId, string carrierStatusCode);

    /// <summary>
    /// requirement-spec.md's resolved ETA computation: the static per-carrier/per-service-level
    /// SLA fallback, used whenever the carrier's webhook/API supplies no ETA of its own.
    /// </summary>
    DateTimeOffset ComputeSlaFallbackEta(string carrierId, DateTimeOffset dispatchedAt);

    string? PollingEndpointTemplate(string carrierId);
}
