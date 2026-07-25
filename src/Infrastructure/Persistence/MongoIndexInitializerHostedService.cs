using KartDeliveryTrackingService.Domain.Tracking;
using KartDeliveryTrackingService.Infrastructure.Persistence.Documents;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;

namespace KartDeliveryTrackingService.Infrastructure.Persistence;

/// <summary>
/// Declares every index database-design.md specifies, once at startup - idempotent (Mongo's
/// <c>createIndex</c> is a no-op if an equivalent index already exists), mirroring
/// RabbitMqTopologyStartupHostedService's "log and swallow, don't crash the process" shape for a
/// datastore outage at boot. Each index is created independently so one failure (e.g. a
/// pre-existing index with an incompatible definition) doesn't prevent the rest from being
/// declared.
/// </summary>
public sealed class MongoIndexInitializerHostedService : IHostedService
{
    private readonly MongoContext _context;
    private readonly ILogger<MongoIndexInitializerHostedService> _logger;

    public MongoIndexInitializerHostedService(MongoContext context, ILogger<MongoIndexInitializerHostedService> logger)
    {
        _context = context;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        // Fire-and-forget, deliberately not awaited: MongoDB's own server-selection timeout
        // (30s default) applies per index, and awaiting this inline would block the generic host
        // from ever starting Kestrel while Mongo is unreachable - directly contradicting this
        // service's own P95<150ms/P99<400ms read-path SLA by making the whole process
        // unavailable for minutes, not just index creation, during a datastore outage at boot.
        _ = DeclareIndexesAsync(cancellationToken);
        return Task.CompletedTask;
    }

    /// <summary>Exposed (not private) so tests can await index creation deterministically instead of racing the fire-and-forget call in <see cref="StartAsync"/>.</summary>
    public async Task DeclareIndexesAsync(CancellationToken cancellationToken)
    {
        // Polling-fallback sweep's "every non-terminal shipment stale past N hours" scan
        // (edge-cases.md "Carrier Webhook Failure or Silence"). Partial-filter expressions only
        // support $eq/$exists/$gt/$gte/$lt/$lte/$type/$and (never $ne/$nin/$not/$or), so
        // "non-terminal" is expressed via the ordinal - not the status string - matching
        // TrackingRecordRepository.GetStaleNonTerminalAsync's own query shape exactly.
        await CreateIndexAsync("tracking_records.lastUpdatedAt (non-terminal)", () =>
        {
            var nonTerminalFilter = Builders<TrackingRecordDocument>.Filter.Lt(d => d.CurrentOrdinal, LifecycleOrdinal.MaxOrdinal);
            return _context.TrackingRecords.Indexes.CreateOneAsync(
                new CreateIndexModel<TrackingRecordDocument>(
                    Builders<TrackingRecordDocument>.IndexKeys.Ascending(d => d.LastUpdatedAt),
                    new CreateIndexOptions<TrackingRecordDocument> { PartialFilterExpression = nonTerminalFilter }),
                cancellationToken: cancellationToken);
        });

        // Append-only identity (trackingId, sequence) - database-enforced backstop.
        await CreateIndexAsync("tracking_status_history_entries.(trackingId,sequence) unique", () =>
            _context.StatusHistoryEntries.Indexes.CreateOneAsync(
                new CreateIndexModel<StatusHistoryEntryDocument>(
                    Builders<StatusHistoryEntryDocument>.IndexKeys.Ascending(d => d.TrackingId).Ascending(d => d.Sequence),
                    new CreateIndexOptions { Unique = true }),
                cancellationToken: cancellationToken));

        // TRK-7's untriaged-unmapped triage queue scan - matches
        // TrackingStatusHistoryRepository.GetUntriagedUnmappedAsync's query shape exactly.
        await CreateIndexAsync("tracking_status_history_entries.receivedAt (untriaged unmapped)", () =>
        {
            var unmappedFilter = Builders<StatusHistoryEntryDocument>.Filter.And(
                Builders<StatusHistoryEntryDocument>.Filter.Eq(d => d.CanonicalStatus, null),
                Builders<StatusHistoryEntryDocument>.Filter.Eq(d => d.IsCarrierSourced, true),
                Builders<StatusHistoryEntryDocument>.Filter.Eq(d => d.EscalatedAt, null));
            return _context.StatusHistoryEntries.Indexes.CreateOneAsync(
                new CreateIndexModel<StatusHistoryEntryDocument>(
                    Builders<StatusHistoryEntryDocument>.IndexKeys.Ascending(d => d.ReceivedAt),
                    new CreateIndexOptions<StatusHistoryEntryDocument> { PartialFilterExpression = unmappedFilter }),
                cancellationToken: cancellationToken);
        });

        // TTL reaper - both eviction rules (7-day baseline; terminal+30-day early eviction).
        await CreateIndexAsync("webhook_dedup_entries.expiresAt TTL", () =>
            _context.WebhookDedupEntries.Indexes.CreateOneAsync(
                new CreateIndexModel<WebhookDedupEntryDocument>(
                    Builders<WebhookDedupEntryDocument>.IndexKeys.Ascending(d => d.ExpiresAt),
                    new CreateIndexOptions { ExpireAfter = TimeSpan.Zero }),
                cancellationToken: cancellationToken));

        // TRK-6's early-eviction recompute lookup.
        await CreateIndexAsync("webhook_dedup_entries.trackingId", () =>
            _context.WebhookDedupEntries.Indexes.CreateOneAsync(
                new CreateIndexModel<WebhookDedupEntryDocument>(Builders<WebhookDedupEntryDocument>.IndexKeys.Ascending(d => d.TrackingId)),
                cancellationToken: cancellationToken));

        // Outbox relay's "pending rows, oldest first" poll.
        await CreateIndexAsync("tracking_outbox_events.(publishedAt,occurredAt)", () =>
            _context.OutboxEvents.Indexes.CreateOneAsync(
                new CreateIndexModel<TrackingOutboxEventDocument>(
                    Builders<TrackingOutboxEventDocument>.IndexKeys.Ascending(d => d.PublishedAt).Ascending(d => d.OccurredAt)),
                cancellationToken: cancellationToken));
    }

    private async Task CreateIndexAsync(string description, Func<Task<string>> createIndex)
    {
        try
        {
            await createIndex();
            _logger.LogInformation("Declared MongoDB index: {Description}.", description);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not declare MongoDB index '{Description}' at startup.", description);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
