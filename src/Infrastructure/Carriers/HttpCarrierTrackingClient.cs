using System.Collections.Concurrent;
using System.Net.Http.Json;
using KartDeliveryTrackingService.Application.Common.Interfaces;
using Microsoft.Extensions.Logging;
using Polly;
using Polly.CircuitBreaker;

namespace KartDeliveryTrackingService.Infrastructure.Carriers;

/// <summary>api-contract.yaml-adjacent: the same generic wire shape assumption as <c>GenericCarrierWebhookBody</c>, applied to a carrier's own tracking-lookup API response.</summary>
public sealed class GenericCarrierPollResponse
{
    public string? StatusCode { get; set; }

    public DateTimeOffset? EventTimestamp { get; set; }
}

/// <summary>
/// TRK-5's outbound polling-fallback client. design-decisions.md "Resilience Pattern for the
/// Carrier Polling Fallback Client": a per-carrier circuit breaker + bounded per-call timeout,
/// bulkhead-isolated so one carrier's open circuit can't consume capacity needed for another
/// carrier's polling. Each carrier gets its own independently-stateful policy instance - that
/// independence *is* the bulkhead isolation here, since polling itself runs from one sequential
/// sweep loop (TRK-5), not many concurrent callers needing a shared concurrency limiter.
/// </summary>
public sealed class HttpCarrierTrackingClient : ICarrierTrackingClient
{
    private static readonly TimeSpan CallTimeout = TimeSpan.FromSeconds(5);
    private const int FailuresBeforeBreak = 5;
    private static readonly TimeSpan BreakDuration = TimeSpan.FromMinutes(1);

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ICarrierRegistry _carrierRegistry;
    private readonly ILogger<HttpCarrierTrackingClient> _logger;
    private readonly ConcurrentDictionary<string, IAsyncPolicy<HttpResponseMessage>> _policies = new();

    public HttpCarrierTrackingClient(IHttpClientFactory httpClientFactory, ICarrierRegistry carrierRegistry, ILogger<HttpCarrierTrackingClient> logger)
    {
        _httpClientFactory = httpClientFactory;
        _carrierRegistry = carrierRegistry;
        _logger = logger;
    }

    public async Task<CarrierPollResult?> PollAsync(string carrierId, string trackingId, CancellationToken cancellationToken)
    {
        var template = _carrierRegistry.PollingEndpointTemplate(carrierId);
        if (string.IsNullOrEmpty(template))
        {
            return null;
        }

        var url = template.Replace("{trackingId}", Uri.EscapeDataString(trackingId), StringComparison.Ordinal);
        var client = _httpClientFactory.CreateClient(nameof(HttpCarrierTrackingClient));
        var policy = GetOrCreatePolicy(carrierId);

        try
        {
            var response = await policy.ExecuteAsync(ct => client.GetAsync(url, ct), cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            var body = await response.Content.ReadFromJsonAsync<GenericCarrierPollResponse>(cancellationToken: cancellationToken);
            if (string.IsNullOrEmpty(body?.StatusCode))
            {
                return null;
            }

            var rawPayload = await response.Content.ReadAsStringAsync(cancellationToken);
            return new CarrierPollResult(body.StatusCode, body.EventTimestamp ?? DateTimeOffset.UtcNow, rawPayload);
        }
        catch (BrokenCircuitException)
        {
            _logger.LogWarning("Circuit open for carrier {CarrierId}; skipping this poll.", carrierId);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Polling fallback request failed for carrier {CarrierId}, tracking {TrackingId}.", carrierId, trackingId);
            return null;
        }
    }

    private IAsyncPolicy<HttpResponseMessage> GetOrCreatePolicy(string carrierId) => _policies.GetOrAdd(carrierId, id =>
    {
        var timeout = Policy.TimeoutAsync<HttpResponseMessage>(CallTimeout);
        var circuitBreaker = Policy<HttpResponseMessage>
            .Handle<HttpRequestException>()
            .Or<TaskCanceledException>()
            .CircuitBreakerAsync(
                FailuresBeforeBreak,
                BreakDuration,
                onBreak: (_, breakDelay) => _logger.LogWarning("Circuit opened for carrier {CarrierId} for {BreakDelay}.", id, breakDelay),
                onReset: () => _logger.LogInformation("Circuit closed for carrier {CarrierId}.", id));

        // Bulkhead isolation is this per-carrier policy instance's own independent state, not a
        // shared Polly BulkheadPolicy - see the type-level remarks.
        return Policy.WrapAsync(circuitBreaker, timeout);
    });
}
