using KartDeliveryTrackingService.Application.Common.Interfaces;
using KartDeliveryTrackingService.Application.Common.Options;
using KartDeliveryTrackingService.Infrastructure.BackgroundServices;
using KartDeliveryTrackingService.Infrastructure.Carriers;
using KartDeliveryTrackingService.Infrastructure.Messaging;
using KartDeliveryTrackingService.Infrastructure.Persistence;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MongoDB.Driver;
using RabbitMQ.Client;

namespace KartDeliveryTrackingService.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<MongoOptions>(configuration.GetSection("Mongo"));
        services.Configure<TrackingOptions>(configuration.GetSection("Tracking"));
        services.Configure<CarrierRegistryOptions>(configuration.GetSection("Carriers"));

        // database-design.md's Architecture Exception: MongoDB is this service's sole durable
        // store - no PostgreSQL DbContext anywhere in this service. ServerSelectionTimeout is
        // lowered from the driver's 30s default to 5s: requirement-spec.md's P95<150ms/P99<400ms
        // read-path SLA means a request should fail fast into the global exception handler during
        // a Mongo outage, not hang for half a minute waiting for server selection.
        services.AddSingleton<IMongoClient>(sp =>
        {
            var settings = MongoClientSettings.FromConnectionString(sp.GetRequiredService<IOptions<MongoOptions>>().Value.ConnectionString);
            settings.ServerSelectionTimeout = TimeSpan.FromSeconds(5);
            return new MongoClient(settings);
        });
        services.AddSingleton(sp =>
        {
            var options = sp.GetRequiredService<IOptions<MongoOptions>>().Value;
            return new MongoContext(sp.GetRequiredService<IMongoClient>().GetDatabase(options.Database));
        });
        services.AddHostedService<MongoIndexInitializerHostedService>();

        services.AddScoped<ITrackingRecordRepository, TrackingRecordRepository>();
        services.AddScoped<ITrackingStatusHistoryRepository, TrackingStatusHistoryRepository>();
        services.AddScoped<IWebhookDedupRepository, WebhookDedupRepository>();
        services.AddScoped<IOutboxEventWriter, OutboxEventWriter>();

        services.AddSingleton<ICarrierRegistry, ConfigCarrierRegistry>();
        services.AddSingleton<ICarrierWebhookVerifier, HmacCarrierWebhookVerifier>();
        services.AddSingleton<ICarrierTrackingClient, HttpCarrierTrackingClient>();
        services.AddHttpClient(nameof(HttpCarrierTrackingClient));

        // contracts/message-bus-manifest.json is the single source of truth for this service's
        // entire RabbitMQ topology - every exchange, queue, binding, dead-letter and retry-tier
        // name. Nothing messaging-related is hardcoded in C#: the manifest is loaded once here
        // and shared as a singleton; RabbitMqTopologyProvisioner scans it to declare the
        // topology. IConnectionFactory only builds config, it does not connect eagerly, so
        // registering it here is safe even if RabbitMQ is unreachable at startup - each hosted
        // service below owns its own retrying connection.
        services.Configure<RabbitMqOptions>(configuration.GetSection("RabbitMq"));
        services.AddSingleton(sp =>
        {
            var options = sp.GetRequiredService<IOptions<RabbitMqOptions>>().Value;
            var manifestPath = Path.IsPathRooted(options.ManifestPath)
                ? options.ManifestPath
                : Path.Combine(AppContext.BaseDirectory, options.ManifestPath);
            return MessageBusManifestLoader.Load(manifestPath);
        });
        services.AddSingleton<IConnectionFactory>(sp => new ConnectionFactory
        {
            HostName = sp.GetRequiredService<IOptions<RabbitMqOptions>>().Value.HostName,
            DispatchConsumersAsync = true,
        });
        services.AddHostedService<RabbitMqTopologyStartupHostedService>();
        services.AddHostedService<OutboxRelayHostedService>();
        services.AddHostedService<ShippingEventsConsumerHostedService>();
        services.AddHostedService<CarrierStatusIngestedConsumerHostedService>();

        // TRK-5 / TRK-7 sweeps - entirely internal to this service.
        services.AddHostedService<PollStaleShipmentsHostedService>();
        services.AddHostedService<EscalateUnmappedStatusAlertsHostedService>();

        return services;
    }
}
