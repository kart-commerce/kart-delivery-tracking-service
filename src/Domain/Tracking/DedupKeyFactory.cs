using System.Security.Cryptography;
using System.Text;

namespace KartDeliveryTrackingService.Domain.Tracking;

/// <summary>
/// edge-cases.md "Duplicate Carrier Webhook Delivery": the carrier-agnostic fallback dedup key -
/// a content hash of <c>trackingId + canonicalStatus-or-raw-code-if-unmapped + carrierEventTimestamp</c>.
/// Used whenever a carrier's own adapter contract supplies no idempotency key of its own.
/// </summary>
public static class DedupKeyFactory
{
    public static string FromContentHash(string trackingId, string statusOrRawCode, DateTimeOffset carrierEventTimestamp)
    {
        var input = $"{trackingId}|{statusOrRawCode}|{carrierEventTimestamp:O}";
        var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }

    /// <summary>Prefers a carrier-supplied idempotency key when the carrier's adapter contract provides one.</summary>
    public static string FromCarrierKey(string carrierId, string carrierSuppliedKey) => $"carrier:{carrierId}:{carrierSuppliedKey}";
}
