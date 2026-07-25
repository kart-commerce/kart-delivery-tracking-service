using MongoDB.Driver;
using Testcontainers.MongoDb;
using Xunit;

namespace KartDeliveryTrackingService.IntegrationTests.Fixtures;

/// <summary>
/// Real MongoDB behavior (atomic findOneAndUpdate ordinal guard, TTL index expiry semantics,
/// partial-index-eligible query shapes) that no fake/in-memory Mongo substitute can faithfully
/// model - mirrors kart-inventory-service's PostgreSqlContainerFixture pattern, adapted to Mongo.
/// </summary>
public sealed class MongoContainerFixture : IAsyncLifetime
{
    private MongoDbContainer? _container;

    public IMongoDatabase Database { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        _container = new MongoDbBuilder().WithImage("mongo:7.0").Build();
        await _container.StartAsync();

        var client = new MongoClient(_container.GetConnectionString());
        Database = client.GetDatabase("kart_delivery_tracking_test");
    }

    public async Task DisposeAsync()
    {
        if (_container is not null)
        {
            await _container.DisposeAsync();
        }
    }
}

[CollectionDefinition("Mongo")]
public sealed class MongoCollection : ICollectionFixture<MongoContainerFixture>
{
}
