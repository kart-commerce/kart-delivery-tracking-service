using FluentAssertions;
using KartDeliveryTrackingService.Domain.Tracking;
using KartDeliveryTrackingService.Infrastructure.Persistence;
using KartDeliveryTrackingService.IntegrationTests.Fixtures;
using Xunit;

namespace KartDeliveryTrackingService.IntegrationTests;

[Collection("Mongo")]
public class WebhookDedupRepositoryTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private readonly WebhookDedupRepository _repository;

    public WebhookDedupRepositoryTests(MongoContainerFixture fixture)
    {
        _repository = new WebhookDedupRepository(new MongoContext(fixture.Database));
    }

    [Fact]
    public async Task InsertAsync_ThenTryGetAsync_RoundTripsTheEntry()
    {
        var dedupKey = $"dedup-{Guid.NewGuid():N}";
        await _repository.InsertAsync(new WebhookDedupEntry(dedupKey, "trk-1", Now, "system:test", Now.AddDays(7)), CancellationToken.None);

        var found = await _repository.TryGetAsync(dedupKey, CancellationToken.None);

        found.Should().NotBeNull();
        found!.TrackingId.Should().Be("trk-1");
    }

    [Fact]
    public async Task TryGetAsync_ForUnknownKey_ReturnsNull()
    {
        var found = await _repository.TryGetAsync($"dedup-{Guid.NewGuid():N}", CancellationToken.None);

        found.Should().BeNull();
    }

    [Fact]
    public async Task RetargetExpiryForTrackingIdAsync_OnlyShortensAnAlreadyEarlierExpiry_NeverExtends()
    {
        // edge-cases.md "Duplicate Carrier Webhook Delivery" / TRK-6: the terminal+30-day
        // recompute must never push a still-live entry's expiry later than it already is.
        var trackingId = $"trk-{Guid.NewGuid():N}";
        var earlyKey = $"dedup-{Guid.NewGuid():N}";
        var lateKey = $"dedup-{Guid.NewGuid():N}";
        await _repository.InsertAsync(new WebhookDedupEntry(earlyKey, trackingId, Now, "system:test", Now.AddDays(2)), CancellationToken.None);
        await _repository.InsertAsync(new WebhookDedupEntry(lateKey, trackingId, Now, "system:test", Now.AddDays(30)), CancellationToken.None);

        await _repository.RetargetExpiryForTrackingIdAsync(trackingId, Now.AddDays(10), Now, "system:delivery-tracking-terminal-status-sweep", CancellationToken.None);

        var early = await _repository.TryGetAsync(earlyKey, CancellationToken.None);
        var late = await _repository.TryGetAsync(lateKey, CancellationToken.None);
        early!.ExpiresAt.Should().Be(Now.AddDays(2), "an already-earlier expiry must never be pushed later");
        late!.ExpiresAt.Should().Be(Now.AddDays(10), "a still-later expiry is shortened to the terminal+grace target");
    }
}
