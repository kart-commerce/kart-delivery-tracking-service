namespace KartDeliveryTrackingService.Application.Common.Interfaces;

public sealed record CarrierPollResult(string CarrierStatusCode, DateTimeOffset EventTimestamp, string RawPayload);

/// <summary>
/// TRK-5: the outbound polling-fallback client (edge-cases.md "Carrier Webhook Failure or
/// Silence"). design-decisions.md's "Resilience Pattern for the Carrier Polling Fallback Client" -
/// per-carrier circuit breaker + bounded per-call timeout, bulkhead-isolated - is applied by the
/// Infrastructure-layer implementation (a named <c>HttpClient</c> + Polly policy per carrier), not
/// visible at this interface.
/// </summary>
public interface ICarrierTrackingClient
{
    Task<CarrierPollResult?> PollAsync(string carrierId, string trackingId, CancellationToken cancellationToken);
}
