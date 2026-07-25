using KartDeliveryTrackingService.Infrastructure.Persistence.Documents;
using MongoDB.Driver;

namespace KartDeliveryTrackingService.Infrastructure.Persistence;

/// <summary>Typed accessor for this service's five MongoDB collections (database-design.md).</summary>
public sealed class MongoContext
{
    public const string TrackingRecordsCollectionName = "tracking_records";
    public const string StatusHistoryEntriesCollectionName = "tracking_status_history_entries";
    public const string StatusHistoryCountersCollectionName = "tracking_status_history_counters";
    public const string WebhookDedupEntriesCollectionName = "webhook_dedup_entries";
    public const string OutboxEventsCollectionName = "tracking_outbox_events";

    public IMongoDatabase Database { get; }

    public MongoContext(IMongoDatabase database)
    {
        Database = database;
    }

    public IMongoCollection<TrackingRecordDocument> TrackingRecords => Database.GetCollection<TrackingRecordDocument>(TrackingRecordsCollectionName);

    public IMongoCollection<StatusHistoryEntryDocument> StatusHistoryEntries => Database.GetCollection<StatusHistoryEntryDocument>(StatusHistoryEntriesCollectionName);

    public IMongoCollection<StatusHistoryCounterDocument> StatusHistoryCounters => Database.GetCollection<StatusHistoryCounterDocument>(StatusHistoryCountersCollectionName);

    public IMongoCollection<WebhookDedupEntryDocument> WebhookDedupEntries => Database.GetCollection<WebhookDedupEntryDocument>(WebhookDedupEntriesCollectionName);

    public IMongoCollection<TrackingOutboxEventDocument> OutboxEvents => Database.GetCollection<TrackingOutboxEventDocument>(OutboxEventsCollectionName);
}
