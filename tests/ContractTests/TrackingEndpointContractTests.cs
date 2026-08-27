using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace KartDeliveryTrackingService.ContractTests;

/// <summary>
/// Verifies live HTTP responses against contracts/api-contract.yaml - mirrors
/// kart-inventory-service's ContractTests shape (a dedicated WebApplicationFactory&lt;Program&gt;
/// subclass, <see cref="DeliveryTrackingApiFactory"/>, swapping DB-backed dependencies for
/// in-memory fakes). Neither endpoint requires authentication (api-contract.yaml: the GET path
/// declares no security scheme; the webhook path explicitly declares `security: []`), so no fake
/// auth handler is needed here, unlike kart-inventory-service's TestAuthenticationHandler.
/// </summary>
public class TrackingEndpointContractTests : IClassFixture<DeliveryTrackingApiFactory>
{
    private readonly HttpClient _client;

    public TrackingEndpointContractTests(DeliveryTrackingApiFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task GetTrackingStatus_ForAnUnmaterializedId_Returns202WithPendingSchemaAndRetryAfterHeader()
    {
        var response = await _client.GetAsync($"/v1/tracking/contract-test-{Guid.NewGuid():N}");

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        response.Headers.RetryAfter.Should().NotBeNull();

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("trackingId").GetString().Should().NotBeNullOrEmpty();
        body.GetProperty("status").GetString().Should().Be("PENDING");
        body.GetProperty("message").GetString().Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task IngestCarrierWebhook_ForUnconfiguredCarrier_Returns404WithErrorResponseSchema()
    {
        var response = await _client.PostAsync(
            $"/internal/v1/webhooks/carriers/contract-test-unconfigured-{Guid.NewGuid():N}",
            new StringContent("{}", Encoding.UTF8, "application/json"));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("code").GetString().Should().NotBeNullOrEmpty();
        body.GetProperty("message").GetString().Should().NotBeNullOrEmpty();
        body.GetProperty("timestamp").GetDateTimeOffset().Should().NotBe(default);
    }

    [Fact]
    public async Task IngestCarrierWebhook_WithoutSignatureHeader_Returns401()
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/internal/v1/webhooks/carriers/demo-carrier")
        {
            Content = new StringContent("""{"trackingId":"trk-1","statusCode":"PICKED_UP"}""", Encoding.UTF8, "application/json"),
        };

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task MetricsEndpoint_IsExposed_ForPrometheusScraping()
    {
        // observability-standards.md's mandatory /metrics.
        var response = await _client.GetAsync("/metrics");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
