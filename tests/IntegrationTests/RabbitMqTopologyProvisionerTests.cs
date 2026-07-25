using FluentAssertions;
using KartDeliveryTrackingService.Infrastructure.Messaging;
using KartDeliveryTrackingService.IntegrationTests.Fixtures;
using Xunit;

namespace KartDeliveryTrackingService.IntegrationTests;

/// <summary>
/// Validates this service's actual, committed contracts/message-bus-manifest.json - the single
/// source of truth for its RabbitMQ topology - against a real broker. A mocked IModel would only
/// prove the C# code calls the right methods; this proves the manifest itself produces a topology
/// RabbitMQ actually accepts.
/// </summary>
[Collection("RabbitMq")]
public class RabbitMqTopologyProvisionerTests
{
    private readonly RabbitMqContainerFixture _fixture;

    public RabbitMqTopologyProvisionerTests(RabbitMqContainerFixture fixture)
    {
        _fixture = fixture;
    }

    private static MessageBusManifest LoadRealManifest() =>
        MessageBusManifestLoader.Load(Path.Combine(AppContext.BaseDirectory, "message-bus-manifest.json"));

    [Fact]
    public void Declare_FromTheRealCommittedManifest_ProvisionsEveryDeclaredQueueAndExchange()
    {
        var manifest = LoadRealManifest();
        using var connection = _fixture.ConnectionFactory.CreateConnection();
        using var channel = connection.CreateModel();

        RabbitMqTopologyProvisioner.Declare(channel, manifest);

        // Passive declare throws if the exchange/queue doesn't actually exist - the real
        // assertion here.
        foreach (var exchange in manifest.Exchanges.Concat(manifest.ExternalExchanges))
        {
            var act = () => channel.ExchangeDeclarePassive(exchange.Name);
            act.Should().NotThrow($"exchange '{exchange.Name}' should have been declared from the manifest");
        }

        foreach (var queue in manifest.Queues)
        {
            var act = () => channel.QueueDeclarePassive(queue.Name);
            act.Should().NotThrow($"queue '{queue.Name}' should have been declared from the manifest");
        }

        foreach (var dlq in manifest.DeadLetterQueues)
        {
            var act = () => channel.QueueDeclarePassive(dlq.Name);
            act.Should().NotThrow($"dead-letter queue '{dlq.Name}' should have been declared from the manifest");
        }
    }

    [Fact]
    public void Declare_IsIdempotent_CallingTwiceOnTheSameChannelDoesNotThrow()
    {
        var manifest = LoadRealManifest();
        using var connection = _fixture.ConnectionFactory.CreateConnection();
        using var channel = connection.CreateModel();

        RabbitMqTopologyProvisioner.Declare(channel, manifest);
        var act = () => RabbitMqTopologyProvisioner.Declare(channel, manifest);

        act.Should().NotThrow();
    }

    [Fact]
    public void Declare_PublishToTrackingExchange_RoutesIntoTheOwnCarrierStatusIngestedConsumerQueue()
    {
        // This is the actual self-publish/self-consume loop design-decisions.md's "Durable
        // Ingestion Buffering Pattern" describes: TRK-3 publishes CarrierStatusIngested onto
        // tracking.exchange, and this service's own queue binds right back to it.
        var manifest = LoadRealManifest();
        using var connection = _fixture.ConnectionFactory.CreateConnection();
        using var channel = connection.CreateModel();
        RabbitMqTopologyProvisioner.Declare(channel, manifest);

        channel.BasicPublish(
            exchange: manifest.ExchangeFor("CarrierStatusIngested"),
            routingKey: manifest.RoutingKeyFor("CarrierStatusIngested"),
            mandatory: false,
            basicProperties: channel.CreateBasicProperties(),
            body: "{}"u8.ToArray());

        // Give RabbitMQ a moment to route the published message onto the bound queue.
        Thread.Sleep(500);
        var messageCount = channel.MessageCount("tracking.carrier-status-ingested.queue");
        messageCount.Should().Be(1u);
    }
}
