using MongoDB.Bson.Serialization.Attributes;

namespace KartDeliveryTrackingService.Infrastructure.Persistence.Documents;

/// <summary>
/// This service's Mongo-backed analog of the EF Core transactional outbox other Kart services use
/// (see IOutboxEventWriter's remarks) - <c>tracking_outbox_events</c>. <c>_id</c> is either a
/// deterministic key (idempotent upsert, when the writer supplies one) or a fresh GUID.
/// </summary>
public sealed class TrackingOutboxEventDocument
{
    [BsonId]
    public string Id { get; set; } = string.Empty;

    [BsonElement("aggregateId")]
    public string AggregateId { get; set; } = string.Empty;

    [BsonElement("eventType")]
    public string EventType { get; set; } = string.Empty;

    [BsonElement("payload")]
    public string Payload { get; set; } = string.Empty;

    [BsonElement("occurredAt")]
    public DateTime OccurredAt { get; set; }

    [BsonElement("publishedAt")]
    public DateTime? PublishedAt { get; set; }
}
