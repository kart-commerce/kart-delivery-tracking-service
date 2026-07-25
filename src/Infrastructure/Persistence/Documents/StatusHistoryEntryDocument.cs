using MongoDB.Bson.Serialization.Attributes;

namespace KartDeliveryTrackingService.Infrastructure.Persistence.Documents;

/// <summary>
/// database-design.md's <c>tracking_status_history_entries</c> collection. <c>_id</c> is a
/// deterministic string (the dedup key, or "{trackingId}:system-anchor" for the one SYSTEM entry)
/// rather than an auto-generated ObjectId, so appends are naturally upsert-idempotent
/// (see IWebhookDedupRepository's remarks on why TRK-4's pipeline is safely re-runnable).
/// </summary>
public sealed class StatusHistoryEntryDocument
{
    [BsonId]
    public string Id { get; set; } = string.Empty;

    [BsonElement("trackingId")]
    public string TrackingId { get; set; } = string.Empty;

    [BsonElement("sequence")]
    public long Sequence { get; set; }

    [BsonElement("carrierStatusCode")]
    public string? CarrierStatusCode { get; set; }

    [BsonElement("canonicalStatus")]
    public string? CanonicalStatus { get; set; }

    [BsonElement("ingestionSource")]
    public string IngestionSource { get; set; } = string.Empty;

    /// <summary>
    /// Denormalized from <see cref="IngestionSource"/> (true for Webhook/Poll, false for System)
    /// purely so the untriaged-unmapped partial index (database-design.md) can be expressed with
    /// an equality clause - MongoDB partial-index filter expressions only support
    /// $eq/$exists/$gt/$gte/$lt/$lte/$type/$and, never $ne/$nin/$not/$or.
    /// </summary>
    [BsonElement("isCarrierSourced")]
    public bool IsCarrierSourced { get; set; }

    [BsonElement("rawPayload")]
    public string? RawPayload { get; set; }

    [BsonElement("eventTimestamp")]
    public DateTime EventTimestamp { get; set; }

    [BsonElement("receivedAt")]
    public DateTime ReceivedAt { get; set; }

    [BsonElement("createdBy")]
    public string CreatedBy { get; set; } = string.Empty;

    [BsonElement("escalatedAt")]
    public DateTime? EscalatedAt { get; set; }
}
