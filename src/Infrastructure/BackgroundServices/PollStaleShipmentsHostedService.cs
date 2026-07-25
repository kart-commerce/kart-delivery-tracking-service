using KartDeliveryTrackingService.Application.Common.Options;
using KartDeliveryTrackingService.Application.Features.PollStaleShipmentStatus;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace KartDeliveryTrackingService.Infrastructure.BackgroundServices;

/// <summary>TRK-5's sweep cadence - see <see cref="PollStaleShipmentsCommand"/> for the actual polling logic.</summary>
public sealed class PollStaleShipmentsHostedService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly TrackingOptions _options;
    private readonly ILogger<PollStaleShipmentsHostedService> _logger;

    public PollStaleShipmentsHostedService(IServiceScopeFactory scopeFactory, IOptions<TrackingOptions> options, ILogger<PollStaleShipmentsHostedService> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromMinutes(_options.PollingIntervalMinutes);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var sender = scope.ServiceProvider.GetRequiredService<ISender>();
                var result = await sender.Send(new PollStaleShipmentsCommand(), stoppingToken);
                if (result.IsSuccess && result.Value > 0)
                {
                    _logger.LogInformation("Polling-fallback sweep processed {Count} stale shipment(s).", result.Value);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Polling-fallback sweep failed; will retry next interval.");
            }

            await Task.Delay(interval, stoppingToken);
        }
    }
}
