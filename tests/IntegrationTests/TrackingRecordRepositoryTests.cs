using FluentAssertions;
using KartDeliveryTrackingService.Domain.Tracking;
using KartDeliveryTrackingService.Infrastructure.Persistence;
using KartDeliveryTrackingService.IntegrationTests.Fixtures;
using Xunit;

namespace KartDeliveryTrackingService.IntegrationTests;

[Collection("Mongo")]
public class TrackingRecordRepositoryTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private readonly TrackingRecordRepository _repository;

    public TrackingRecordRepositoryTests(MongoContainerFixture fixture)
    {
        _repository = new TrackingRecordRepository(new MongoContext(fixture.Database));
    }

    private static TrackingRecord NewRecord(string trackingId) =>
        TrackingRecord.Create(trackingId, "order-1", "demo-carrier", new Eta(Now.AddDays(5), EtaSource.SlaFallback), Now, "system:shipment-dispatched-consumer").Value;

    [Fact]
    public async Task CreateIfNotExistsAsync_SecondCallForSameTrackingId_ReportsNotCreated_RealDuplicateKeyBehavior()
    {
        var trackingId = $"trk-{Guid.NewGuid():N}";

        var first = await _repository.CreateIfNotExistsAsync(NewRecord(trackingId), CancellationToken.None);
        var second = await _repository.CreateIfNotExistsAsync(NewRecord(trackingId), CancellationToken.None);

        first.WasCreated.Should().BeTrue();
        second.WasCreated.Should().BeFalse();
    }

    [Fact]
    public async Task TryApplyOrdinalGuardedUpdateAsync_AtomicUnderConcurrentAttempts_OnlyOneAppliesPerOrdinalStep()
    {
        // design-decisions.md "Concurrency Control for the Status-Regression Guard" - the whole
        // point of the atomic findOneAndUpdate is that concurrent attempts to advance from the
        // same ordinal can't both succeed. This is exactly the kind of real-locking behavior an
        // in-memory fake can't faithfully model.
        var trackingId = $"trk-{Guid.NewGuid():N}";
        await _repository.CreateIfNotExistsAsync(NewRecord(trackingId), CancellationToken.None);
        var eta = new Eta(Now.AddDays(3), EtaSource.SlaFallback);

        var attempts = await Task.WhenAll(Enumerable.Range(0, 10).Select(_ =>
            _repository.TryApplyOrdinalGuardedUpdateAsync(trackingId, CanonicalDeliveryStatus.InTransit, 2, eta, Now.AddHours(1), "system:test", CancellationToken.None)));

        attempts.Count(applied => applied).Should().Be(1);
        var record = await _repository.GetAsync(trackingId, CancellationToken.None);
        record!.CurrentStatus.Should().Be(CanonicalDeliveryStatus.InTransit);
    }

    [Fact]
    public async Task TryApplyOrdinalGuardedUpdateAsync_RegressingOrdinal_IsRejected()
    {
        var trackingId = $"trk-{Guid.NewGuid():N}";
        await _repository.CreateIfNotExistsAsync(NewRecord(trackingId), CancellationToken.None);
        var eta = new Eta(Now.AddDays(3), EtaSource.SlaFallback);
        await _repository.TryApplyOrdinalGuardedUpdateAsync(trackingId, CanonicalDeliveryStatus.OutForDelivery, 3, eta, Now.AddHours(1), "system:test", CancellationToken.None);

        var applied = await _repository.TryApplyOrdinalGuardedUpdateAsync(trackingId, CanonicalDeliveryStatus.InTransit, 2, eta, Now.AddHours(2), "system:test", CancellationToken.None);

        applied.Should().BeFalse();
        var record = await _repository.GetAsync(trackingId, CancellationToken.None);
        record!.CurrentStatus.Should().Be(CanonicalDeliveryStatus.OutForDelivery);
    }

    [Fact]
    public async Task GetStaleNonTerminalAsync_ExcludesTerminalAndFreshRecords()
    {
        var staleTrackingId = $"trk-{Guid.NewGuid():N}";
        var freshTrackingId = $"trk-{Guid.NewGuid():N}";
        var terminalTrackingId = $"trk-{Guid.NewGuid():N}";

        await _repository.CreateIfNotExistsAsync(NewRecord(staleTrackingId), CancellationToken.None);
        await _repository.CreateIfNotExistsAsync(NewRecord(freshTrackingId), CancellationToken.None);
        await _repository.CreateIfNotExistsAsync(NewRecord(terminalTrackingId), CancellationToken.None);

        var eta = new Eta(Now.AddDays(1), EtaSource.SlaFallback);
        // Push the "stale" record's lastUpdatedAt into the past by applying an update stamped
        // with an old timestamp.
        await _repository.TryApplyOrdinalGuardedUpdateAsync(staleTrackingId, CanonicalDeliveryStatus.InTransit, 2, eta, Now.AddHours(-10), "system:test", CancellationToken.None);
        await _repository.TryApplyOrdinalGuardedUpdateAsync(terminalTrackingId, CanonicalDeliveryStatus.Delivered, 4, eta, Now.AddHours(-10), "system:test", CancellationToken.None);

        var stale = await _repository.GetStaleNonTerminalAsync(Now.AddHours(-6), 100, CancellationToken.None);

        stale.Select(r => r.TrackingId).Should().Contain(staleTrackingId);
        stale.Select(r => r.TrackingId).Should().NotContain(freshTrackingId);
        stale.Select(r => r.TrackingId).Should().NotContain(terminalTrackingId);
    }
}
