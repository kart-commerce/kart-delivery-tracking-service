namespace KartDeliveryTrackingService.Application.Common.Interfaces;

/// <summary>
/// requirement-spec.md S5: <c>X-Carrier-Signature</c> HMAC-SHA256 verification, keyed by a
/// per-carrier shared secret, checked before any processing. A request failing verification is
/// rejected 401 with no history write and no dedup-store entry.
/// </summary>
public interface ICarrierWebhookVerifier
{
    bool Verify(string carrierId, byte[] rawBody, string? signatureHeader);
}
