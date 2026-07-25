using FluentAssertions;
using KartDeliveryTrackingService.Infrastructure.Persistence;
using KartDeliveryTrackingService.IntegrationTests.Fixtures;
using Microsoft.Extensions.Logging.Abstractions;
using MongoDB.Bson;
using MongoDB.Driver;
using Xunit;

namespace KartDeliveryTrackingService.IntegrationTests;

/// <summary>
/// Regression coverage for a real bug caught only against a live server: MongoDB partial-index
/// filter expressions reject $nin/$ne (only $eq/$exists/$gt/$gte/$lt/$lte/$type/$and are
/// supported), which a naive in-memory fake would never have surfaced.
/// </summary>
[Collection("Mongo")]
public class MongoIndexInitializerHostedServiceTests
{
    private readonly MongoContext _context;

    public MongoIndexInitializerHostedServiceTests(MongoContainerFixture fixture)
    {
        _context = new MongoContext(fixture.Database);
    }

    [Fact]
    public async Task StartAsync_DeclaresEveryIndexAgainstARealServer_WithoutError()
    {
        var service = new MongoIndexInitializerHostedService(_context, NullLogger<MongoIndexInitializerHostedService>.Instance);

        // Calls DeclareIndexesAsync directly (not StartAsync, which is deliberately
        // fire-and-forget so it never blocks Kestrel from starting - see its own remarks) so this
        // assertion runs deterministically rather than racing a detached task.
        await service.DeclareIndexesAsync(CancellationToken.None);

        var trackingRecordIndexes = await (await _context.TrackingRecords.Indexes.ListAsync()).ToListAsync();
        trackingRecordIndexes.Should().Contain(i => i["name"].AsString == "lastUpdatedAt_1");

        var historyIndexes = await (await _context.StatusHistoryEntries.Indexes.ListAsync()).ToListAsync();
        historyIndexes.Should().Contain(i => i["name"] == "trackingId_1_sequence_1");
        historyIndexes.Should().Contain(i => i["name"] == "receivedAt_1");

        var dedupIndexes = await (await _context.WebhookDedupEntries.Indexes.ListAsync()).ToListAsync();
        dedupIndexes.Should().Contain(i => i["name"] == "expiresAt_1" && i.Contains("expireAfterSeconds"));
    }
}
