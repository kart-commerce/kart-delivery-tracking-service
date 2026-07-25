using RabbitMQ.Client;
using Testcontainers.RabbitMq;
using Xunit;

namespace KartDeliveryTrackingService.IntegrationTests.Fixtures;

/// <summary>Real RabbitMQ broker behavior for topology-declaration tests - a mock IModel can't verify the manifest actually produces valid AMQP declarations.</summary>
public sealed class RabbitMqContainerFixture : IAsyncLifetime
{
    private RabbitMqContainer? _container;

    public IConnectionFactory ConnectionFactory { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        _container = new RabbitMqBuilder().WithImage("rabbitmq:3-management-alpine").Build();
        await _container.StartAsync();

        ConnectionFactory = new ConnectionFactory { Uri = new Uri(_container.GetConnectionString()) };
    }

    public async Task DisposeAsync()
    {
        if (_container is not null)
        {
            await _container.DisposeAsync();
        }
    }
}

[CollectionDefinition("RabbitMq")]
public sealed class RabbitMqCollection : ICollectionFixture<RabbitMqContainerFixture>
{
}
