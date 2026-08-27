using KartDeliveryTrackingService.Application.Common.Interfaces;
using KartDeliveryTrackingService.ContractTests.Fakes;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace KartDeliveryTrackingService.ContractTests;

/// <summary>
/// Boots the real Api + Application pipeline (Program.cs unchanged), swapping the real
/// MongoDB-backed <see cref="ITrackingRecordRepository"/> for an in-memory fake - mirrors
/// kart-inventory-service's InventoryApiFactory shape. ContractTests asserts HTTP wire-shape only
/// (status codes, JSON field names, headers), never a real datastore: a bare
/// WebApplicationFactory&lt;Program&gt; would let GetTrackingStatusQueryHandler's real
/// TrackingRecordRepository attempt an actual MongoDB connection, which only "worked" if a stray
/// local/dev Mongo instance happened to be reachable - environment-dependent, and correctly fails
/// (500, not the contracted 202) in a clean CI runner with no database reachable.
///
/// Also removes every registered <see cref="IHostedService"/>: none of them (RabbitMQ topology
/// provisioning, the outbox relay, the Shipping/CarrierStatus consumers, the TRK-5/TRK-7 sweeps,
/// the Mongo index initializer) can reach a real MongoDB/RabbitMQ in this test process, and - while
/// each already "logs and swallows" rather than crashing host startup - there is no reason to let
/// them spin up background connection attempts for tests that only exercise HTTP wire-shape.
/// </summary>
public sealed class DeliveryTrackingApiFactory : WebApplicationFactory<Program>
{
    public InMemoryTrackingRecordRepository TrackingRecords { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            services.RemoveAll(typeof(IHostedService));

            services.RemoveAll(typeof(ITrackingRecordRepository));
            services.AddSingleton<ITrackingRecordRepository>(TrackingRecords);
        });
    }
}
