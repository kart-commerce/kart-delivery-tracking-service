using FluentAssertions;
using KartDeliveryTrackingService.Domain.Tracking;
using KartDeliveryTrackingService.Infrastructure.Persistence;
using KartDeliveryTrackingService.IntegrationTests.Fixtures;
using Xunit;

namespace KartDeliveryTrackingService.IntegrationTests;

[Collection("Mongo")]
public class TrackingStatusHistoryRepositoryTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private readonly TrackingStatusHistoryRepository _repository;

    public TrackingStatusHistoryRepositoryTests(MongoContainerFixture fixture)
    {
        _repository = new TrackingStatusHistoryRepository(new MongoContext(fixture.Database));
    }

    [Fact]
    public async Task NextSequenceAsync_IsMonotonicPerTrackingId_AndIndependentAcrossTrackingIds()
    {
        var trackingIdA = $"trk-{Guid.NewGuid():N}";
        var trackingIdB = $"trk-{Guid.NewGuid():N}";

        var a1 = await _repository.NextSequenceAsync(trackingIdA, CancellationToken.None);
        var a2 = await _repository.NextSequenceAsync(trackingIdA, CancellationToken.None);
        var b1 = await _repository.NextSequenceAsync(trackingIdB, CancellationToken.None);

        a1.Should().Be(1);
        a2.Should().Be(2);
        b1.Should().Be(1);
    }

    [Fact]
    public async Task AppendAsync_CalledTwiceWithSameDeterministicId_UpsertsRatherThanDuplicating()
    {
        var trackingId = $"trk-{Guid.NewGuid():N}";
        var deterministicId = $"dedup-{Guid.NewGuid():N}";
        var entry = new StatusHistoryEntry(trackingId, 1, "IN_TRANSIT", CanonicalDeliveryStatus.InTransit, IngestionSource.Webhook, "raw", Now, Now, "system:test");

        await _repository.AppendAsync(deterministicId, entry, CancellationToken.None);
        await _repository.AppendAsync(deterministicId, entry, CancellationToken.None);

        var candidates = await _repository.GetUntriagedUnmappedAsync(100, CancellationToken.None);
        candidates.Should().NotContain(e => e.TrackingId == trackingId, "this entry is mapped, not unmapped - sanity check it wasn't duplicated into the wrong bucket either");
    }

    [Fact]
    public async Task GetUntriagedUnmappedAsync_ReturnsOnlyNonSystemNullCanonicalNotYetEscalated()
    {
        var mappedId = $"dedup-{Guid.NewGuid():N}";
        var unmappedId = $"dedup-{Guid.NewGuid():N}";
        var systemAnchorId = $"trk-{Guid.NewGuid():N}:system-anchor";
        var escalatedId = $"dedup-{Guid.NewGuid():N}";

        var trackingId = $"trk-{Guid.NewGuid():N}";
        await _repository.AppendAsync(mappedId, new StatusHistoryEntry(trackingId, 1, "IN_TRANSIT", CanonicalDeliveryStatus.InTransit, IngestionSource.Webhook, "raw", Now, Now, "system:test"), CancellationToken.None);
        await _repository.AppendAsync(unmappedId, new StatusHistoryEntry(trackingId, 2, "WEIRD", null, IngestionSource.Webhook, "raw", Now, Now, "system:test"), CancellationToken.None);
        await _repository.AppendAsync(systemAnchorId, new StatusHistoryEntry(trackingId, 0, null, CanonicalDeliveryStatus.Dispatched, IngestionSource.System, null, Now, Now, "system:test"), CancellationToken.None);
        await _repository.AppendAsync(escalatedId, new StatusHistoryEntry(trackingId, 3, "WEIRD2", null, IngestionSource.Poll, "raw", Now, Now, "system:test", escalatedAt: Now), CancellationToken.None);

        var candidates = await _repository.GetUntriagedUnmappedAsync(100, CancellationToken.None);
        var thisTrackingsCandidates = candidates.Where(c => c.TrackingId == trackingId).ToList();

        thisTrackingsCandidates.Should().ContainSingle();
        thisTrackingsCandidates.Single().Sequence.Should().Be(2);
    }

    [Fact]
    public async Task MarkEscalatedAsync_RemovesEntryFromFutureUntriagedScans()
    {
        var trackingId = $"trk-{Guid.NewGuid():N}";
        var deterministicId = $"dedup-{Guid.NewGuid():N}";
        await _repository.AppendAsync(deterministicId, new StatusHistoryEntry(trackingId, 1, "WEIRD", null, IngestionSource.Webhook, "raw", Now, Now, "system:test"), CancellationToken.None);

        await _repository.MarkEscalatedAsync(trackingId, 1, Now, CancellationToken.None);

        var candidates = await _repository.GetUntriagedUnmappedAsync(100, CancellationToken.None);
        candidates.Should().NotContain(c => c.TrackingId == trackingId);
    }
}
