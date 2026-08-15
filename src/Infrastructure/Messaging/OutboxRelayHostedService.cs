using System.Text;
using Kart.Shared.Messaging;
using Kart.Shared.Observability;
using KartDeliveryTrackingService.Application.Common;
using KartDeliveryTrackingService.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;
using RabbitMQ.Client;

namespace KartDeliveryTrackingService.Infrastructure.Messaging;

/// <summary>
/// Relays tracking_outbox_events rows to tracking.exchange - this service's Mongo-backed analog of
/// the EF Core transactional outbox other Kart services use (there is no PostgreSQL DbContext
/// here to hook a SaveChanges interceptor into - architecture.md's Boundary Rationale). Re-declares
/// the manifest's topology idempotently on every (re)connect. Retries indefinitely (every poll
/// tick) until RabbitMQ is reachable, rather than dead-lettering - the publish-side half of
/// at-least-once delivery. Mirrors kart-inventory-service's OutboxRelayHostedService shape.
/// </summary>
public sealed class OutboxRelayHostedService : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan ReconnectDelay = TimeSpan.FromSeconds(10);
    private const int BatchSize = 50;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConnectionFactory _connectionFactory;
    private readonly MessageBusManifest _manifest;
    private readonly ILogger<OutboxRelayHostedService> _logger;

    public OutboxRelayHostedService(
        IServiceScopeFactory scopeFactory,
        IConnectionFactory connectionFactory,
        MessageBusManifest manifest,
        ILogger<OutboxRelayHostedService> logger)
    {
        _scopeFactory = scopeFactory;
        _connectionFactory = connectionFactory;
        _manifest = manifest;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var connection = _connectionFactory.CreateConnection();
                using var channel = connection.CreateModel();
                RabbitMqTopologyProvisioner.Declare(channel, _manifest);

                await RunRelayLoopAsync(channel, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Tracking outbox relay lost its RabbitMQ connection; reconnecting in {Delay}.", ReconnectDelay);
                await Task.Delay(ReconnectDelay, stoppingToken);
            }
        }
    }

    private async Task RunRelayLoopAsync(IModel channel, CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await RelayPendingBatchAsync(channel, stoppingToken);
            await Task.Delay(PollInterval, stoppingToken);
        }
    }

    private async Task RelayPendingBatchAsync(IModel channel, CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MongoContext>();

        var pending = await context.OutboxEvents
            .Find(e => e.PublishedAt == null)
            .SortBy(e => e.OccurredAt)
            .Limit(BatchSize)
            .ToListAsync(cancellationToken);

        foreach (var outboxEvent in pending)
        {
            var exchange = _manifest.ExchangeFor(outboxEvent.EventType);
            var routingKey = _manifest.RoutingKeyFor(outboxEvent.EventType);

            var properties = channel.CreateBasicProperties();
            properties.Persistent = true;
            properties.MessageId = outboxEvent.Id;
            properties.ContentType = "application/json";

            // Every event type this relay ever publishes (CarrierStatusIngested,
            // UnmappedCarrierStatusFlagged, DeliveryStatusUpdated) belongs to business-flows.md
            // flow #8, "Shipping, Warehouse & Fulfillment".
            using var _ = KartFlowContext.Push(FlowNames.ShippingWarehouseFulfillment);

            channel.BasicPublish(
                exchange: exchange,
                routingKey: routingKey,
                basicProperties: properties,
                body: Encoding.UTF8.GetBytes(outboxEvent.Payload));

            var update = Builders<Persistence.Documents.TrackingOutboxEventDocument>.Update.Set(d => d.PublishedAt, DateTime.UtcNow);
            await context.OutboxEvents.UpdateOneAsync(d => d.Id == outboxEvent.Id, update, cancellationToken: cancellationToken);

            _logger.LogInformation(
                "Stage {Stage}: outbox event {EventId} of type {EventType} published to {Exchange}/{RoutingKey}",
                "OutboxEventPublished",
                outboxEvent.Id,
                outboxEvent.EventType,
                exchange,
                routingKey);
        }
    }
}
