using System.Text;
using System.Text.Json;
using Kart.Shared.Messaging;
using Kart.Shared.Observability;
using KartDeliveryTrackingService.Application.Common;
using KartDeliveryTrackingService.Application.Common.Models;
using KartDeliveryTrackingService.Application.Features.ApplyCarrierStatusUpdate;
using KartDeliveryTrackingService.Domain.Tracking;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace KartDeliveryTrackingService.Infrastructure.Messaging;

/// <summary>
/// TRK-4's message-driven entry point: consumes tracking.carrier-status-ingested.queue - this
/// service's own internal event, published by TRK-3's webhook handler onto tracking.exchange and
/// consumed here by the same service (design-decisions.md "Durable Ingestion Buffering Pattern").
/// A failure here (including a not-yet-materialized TrackingRecord race,
/// <see cref="Application.Common.Exceptions.TrackingRecordNotYetMaterializedException"/>) is
/// handled identically to any other - routed through the retry ladder, eventually dead-lettered;
/// the 6-hour polling fallback (TRK-5) re-establishes correctness independent of this queue.
/// </summary>
public sealed class CarrierStatusIngestedConsumerHostedService : BackgroundService
{
    private const string QueueName = "tracking.carrier-status-ingested.queue";
    private const string RetryCountHeader = "x-tracking-carrier-status-ingested-retry-count";

    private static readonly TimeSpan ReconnectDelay = TimeSpan.FromSeconds(10);
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConnectionFactory _connectionFactory;
    private readonly MessageBusManifest _manifest;
    private readonly ILogger<CarrierStatusIngestedConsumerHostedService> _logger;

    public CarrierStatusIngestedConsumerHostedService(
        IServiceScopeFactory scopeFactory,
        IConnectionFactory connectionFactory,
        MessageBusManifest manifest,
        ILogger<CarrierStatusIngestedConsumerHostedService> logger)
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
                _logger.LogError(ex, "Carrier-status-ingested consumer lost its RabbitMQ connection; reconnecting in {Delay}.", ReconnectDelay);
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

            var payload = JsonSerializer.Deserialize<CarrierStatusIngestedEventPayload>(json, SerializerOptions)
                ?? throw new InvalidOperationException("CarrierStatusIngested payload deserialized to null.");

            // business-flows.md flow #8, "Shipping, Warehouse & Fulfillment" - this consumer's entry point.
            using var _ = KartFlowContext.Push(FlowNames.ShippingWarehouseFulfillment);
            _logger.LogInformation(
                "Stage {Stage}: consumed CarrierStatusIngested from {Queue} for tracking {TrackingId} (carrier {CarrierId})",
                "CarrierStatusIngestedEventConsumed",
                QueueName,
                payload.TrackingId,
                payload.CarrierId);

            var command = new ApplyCarrierStatusUpdateCommand(
                payload.CarrierId,
                payload.TrackingId,
                payload.CarrierStatusCode,
                payload.EventTimestamp,
                payload.RawPayload,
                IngestionSource.Webhook);
            var result = await sender.Send(command, stoppingToken);

            if (result.IsFailure)
            {
                throw new InvalidOperationException($"ApplyCarrierStatusUpdate failed: {result.Error.Code} - {result.Error.Message}");
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

            _logger.LogWarning(ex, "Stage {Stage}: handling CarrierStatusIngested failed; routed to retry tier {Tier} (attempt {Attempt}).", "CarrierStatusIngestedHandlingRetried", tier.Name, retryCount + 1);
        }
        else
        {
            _logger.LogCritical(ex, "Stage {Stage}: handling CarrierStatusIngested failed after exhausting all retry tiers; dead-lettering. The polling fallback will re-establish correctness for this tracking id.", "CarrierStatusIngestedHandlingDeadLettered");
            channel.BasicNack(deliverEventArgs.DeliveryTag, multiple: false, requeue: false);
        }
    }
}
