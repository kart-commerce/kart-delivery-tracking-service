using System.Security.Cryptography;
using System.Text;
using KartDeliveryTrackingService.Application.Common.Interfaces;

namespace KartDeliveryTrackingService.Infrastructure.Carriers;

/// <summary>requirement-spec.md S5: X-Carrier-Signature HMAC-SHA256 verification of the raw request body, keyed by a per-carrier shared secret.</summary>
public sealed class HmacCarrierWebhookVerifier : ICarrierWebhookVerifier
{
    private readonly ICarrierRegistry _carrierRegistry;

    public HmacCarrierWebhookVerifier(ICarrierRegistry carrierRegistry)
    {
        _carrierRegistry = carrierRegistry;
    }

    public bool Verify(string carrierId, byte[] rawBody, string? signatureHeader)
    {
        if (string.IsNullOrEmpty(signatureHeader) || !_carrierRegistry.TryGetWebhookSharedSecret(carrierId, out var secret))
        {
            return false;
        }

        var expected = HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), rawBody);
        if (!TryDecodeHex(signatureHeader, out var provided))
        {
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(expected, provided);
    }

    private static bool TryDecodeHex(string value, out byte[] bytes)
    {
        try
        {
            bytes = Convert.FromHexString(value);
            return true;
        }
        catch (FormatException)
        {
            bytes = Array.Empty<byte>();
            return false;
        }
    }
}
