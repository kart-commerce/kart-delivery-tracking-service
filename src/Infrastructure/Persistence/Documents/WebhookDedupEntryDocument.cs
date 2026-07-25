using MongoDB.Bson.Serialization.Attributes;

namespace KartDeliveryTrackingService.Infrastructure.Persistence.Documents;

/// <summary>database-design.md's <c>webhook_dedup_entries</c> collection - <c>_id = dedupKey</c>, TTL-indexed on <c>expiresAt</c>.</summary>
public sealed class WebhookDedupEntryDocument
{
    [BsonId]
    public string Id { get; set; } = string.Empty;

    [BsonElement("trackingId")]
    public string TrackingId { get; set; } = string.Empty;

    [BsonElement("createdAt")]
    public DateTime CreatedAt { get; set; }

    [BsonElement("createdBy")]
    public string CreatedBy { get; set; } = string.Empty;

    [BsonElement("expiresAt")]
    public DateTime ExpiresAt { get; set; }

    [BsonElement("updatedAt")]
    public DateTime? UpdatedAt { get; set; }

    [BsonElement("updatedBy")]
    public string? UpdatedBy { get; set; }
}
