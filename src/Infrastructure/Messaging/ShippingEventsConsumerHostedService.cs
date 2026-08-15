using System.Text;
using System.Text.Json;
using Kart.Shared.Messaging;
using Kart.Shared.Observability;
using KartDeliveryTrackingService.Application.Common;
using KartDeliveryTrackingService.Application.Common.Models;
using KartDeliveryTrackingService.Application.Features.CreateTrackingRecordOnShipmentDispatched;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace KartDeliveryTrackingService.Infrastructure.Messaging;

/// <summary>
/// TRK-1: consumes tracking.shipping-events.queue (bound to shipping.exchange's
/// shipping.shipment.dispatched routing key, per contracts/message-bus-manifest.json) and
/// dispatches to <see cref="CreateTrackingRecordOnShipmentDispatchedCommand"/> via MediatR. On
/// handler failure, walks the manifest's retry ladder (a custom retry-count header, since RabbitMQ
/// has no built-in redelivery counter for this shape) and dead-letters once every tier is
/// exhausted - mirrors kart-inventory-service's OrderEventsConsumerHostedService.
/// </summary>
public sealed class ShippingEventsConsumerHostedService : BackgroundService
{
    private const string QueueName = "tracking.shipping-events.queue";
    private const string RetryCountHeader = "x-tracking-shipping-events-retry-count";

    private static readonly TimeSpan ReconnectDelay = TimeSpan.FromSeconds(10);
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConnectionFactory _connectionFactory;
    private readonly MessageBusManifest _manifest;
    private readonly ILogger<ShippingEventsConsumerHostedService> _logger;

    public ShippingEventsConsumerHostedService(
        IServiceScopeFactory scopeFactory,
        IConnectionFactory connectionFactory,
        MessageBusManifest manifest,
        ILogger<ShippingEventsConsumerHostedService> logger)
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

                var consumer = new AsyncEventingBasicConsumer(channel);
                consumer.Received += async (_, deliverEventArgs) => await OnMessageReceivedAsync(channel, deliverEventArgs, stoppingToken);
                channel.BasicConsume(QueueName, autoAck: false, consumer);

                while (!stoppingToken.IsCancellationRequested && connection.IsOpen)
                {
                    await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Shipping-events consumer lost its RabbitMQ connection; reconnecting in {Delay}.", ReconnectDelay);
                await Task.Delay(ReconnectDelay, stoppingToken);
            }
        }
    }

    private async Task OnMessageReceivedAsync(IModel channel, BasicDeliverEventArgs deliverEventArgs, CancellationToken stoppingToken)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var sender = scope.ServiceProvider.GetRequiredService<ISender>();
            var json = Encoding.UTF8.GetString(deliverEventArgs.Body.Span);

            var payload = JsonSerializer.Deserialize<ShipmentDispatchedEventPayload>(json, SerializerOptions)
                ?? throw new InvalidOperationException("ShipmentDispatched payload deserialized to null.");

            // business-flows.md flow #8, "Shipping, Warehouse & Fulfillment" - this consumer's
            // entry point, pushed once here per checkpoint-logging-standard.md.
            using var _ = KartFlowContext.Push(FlowNames.ShippingWarehouseFulfillment);
            _logger.LogInformation(
                "Stage {Stage}: consumed ShipmentDispatched from {Queue} for order {OrderId}, tracking {TrackingId}",
                "ShipmentDispatchedEventConsumed",
                QueueName,
                payload.OrderId,
                payload.TrackingId);

            var command = new CreateTrackingRecordOnShipmentDispatchedCommand(payload.OrderId, payload.Carrier, payload.TrackingId);
            _logger.LogInformation(
                "Stage {Stage}: dispatching CreateTrackingRecordOnShipmentDispatchedCommand for {TrackingId}",
                "CreateTrackingRecordOnShipmentDispatchedCommandDispatched",
                payload.TrackingId);
            var result = await sender.Send(command, stoppingToken);

            if (result.IsFailure)
            {
                throw new InvalidOperationException($"CreateTrackingRecordOnShipmentDispatched failed: {result.Error.Code} - {result.Error.Message}");
            }

            channel.BasicAck(deliverEventArgs.DeliveryTag, multiple: false);
        }
        catch (Exception ex)
        {
            HandleFailure(channel, deliverEventArgs, ex);
        }
    }

    private void HandleFailure(IModel channel, BasicDeliverEventArgs deliverEventArgs, Exception ex)
    {
        var retryCount = RetryHeaders.GetRetryCount(deliverEventArgs.BasicProperties, RetryCountHeader);
        var tiers = _manifest.GetQueue(QueueName).RetryLadder?.Tiers ?? Array.Empty<RetryTierDefinition>();

        if (retryCount < tiers.Count)
        {
            var tier = tiers[retryCount];
            var properties = channel.CreateBasicProperties();
            properties.Persistent = true;
            properties.Headers = new Dictionary<string, object> { [RetryCountHeader] = retryCount + 1 };

            channel.BasicPublish(exchange: string.Empty, routingKey: tier.Name, basicProperties: properties, body: deliverEventArgs.Body);
            channel.BasicAck(deliverEventArgs.DeliveryTag, multiple: false);

            _logger.LogWarning(ex, "Stage {Stage}: handling ShipmentDispatched failed; routed to retry tier {Tier} (attempt {Attempt}).", "ShipmentDispatchedHandlingRetried", tier.Name, retryCount + 1);
        }
        else
        {
            _logger.LogCritical(ex, "Stage {Stage}: handling ShipmentDispatched failed after exhausting all retry tiers; dead-lettering.", "ShipmentDispatchedHandlingDeadLettered");
            channel.BasicNack(deliverEventArgs.DeliveryTag, multiple: false, requeue: false);
        }
    }
}
