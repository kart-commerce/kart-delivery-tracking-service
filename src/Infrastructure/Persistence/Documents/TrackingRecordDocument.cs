using MongoDB.Bson.Serialization.Attributes;

namespace KartDeliveryTrackingService.Infrastructure.Persistence.Documents;

/// <summary>database-design.md's <c>tracking_records</c> collection - <c>_id = trackingId</c> directly.</summary>
public sealed class TrackingRecordDocument
{
    [BsonId]
    public string Id { get; set; } = string.Empty;

    [BsonElement("orderId")]
    public string OrderId { get; set; } = string.Empty;

    [BsonElement("carrierId")]
    public string CarrierId { get; set; } = string.Empty;

    [BsonElement("currentStatus")]
    public string CurrentStatus { get; set; } = string.Empty;

    [BsonElement("currentOrdinal")]
    public int CurrentOrdinal { get; set; }

    [BsonElement("eta")]
    public EtaDocument Eta { get; set; } = new();

    [BsonElement("lastUpdatedAt")]
    public DateTime LastUpdatedAt { get; set; }

    [BsonElement("createdAt")]
    public DateTime CreatedAt { get; set; }

    [BsonElement("createdBy")]
    public string CreatedBy { get; set; } = string.Empty;

    [BsonElement("updatedBy")]
    public string UpdatedBy { get; set; } = string.Empty;
}

public sealed class EtaDocument
{
    [BsonElement("value")]
    public DateTime Value { get; set; }

    [BsonElement("source")]
    public string Source { get; set; } = string.Empty;
}
