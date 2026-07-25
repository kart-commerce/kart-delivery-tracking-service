using MongoDB.Bson.Serialization.Attributes;

namespace KartDeliveryTrackingService.Infrastructure.Persistence.Documents;

/// <summary>
/// database-design.md's "Sequence Assignment" - <c>tracking_status_history_counters</c>, a bare
/// atomic counter (the platform-wide equivalent of a PostgreSQL SEQUENCE), one document per
/// trackingId. No audit columns - see database-design.md's note on why none are needed here.
/// </summary>
public sealed class StatusHistoryCounterDocument
{
    [BsonId]
    public string Id { get; set; } = string.Empty;

    [BsonElement("nextSequence")]
    public long NextSequence { get; set; }
}
