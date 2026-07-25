namespace KartDeliveryTrackingService.Infrastructure.Persistence;

/// <summary>
/// Binds the "Mongo" configuration section. database-design.md's Architecture Exception: this
/// service has no PostgreSQL write side - MongoDB is its sole durable source of truth.
/// </summary>
public sealed class MongoOptions
{
    public string ConnectionString { get; set; } = "mongodb://localhost:27017";

    public string Database { get; set; } = "kart_delivery_tracking";
}
