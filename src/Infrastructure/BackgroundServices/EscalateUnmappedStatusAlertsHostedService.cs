using KartDeliveryTrackingService.Application.Common.Options;
using KartDeliveryTrackingService.Application.Features.EscalateUnmappedStatusAlerts;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace KartDeliveryTrackingService.Infrastructure.BackgroundServices;

/// <summary>TRK-7's sweep cadence - see <see cref="EscalateUnmappedStatusAlertsCommand"/> for the actual escalation logic.</summary>
public sealed class EscalateUnmappedStatusAlertsHostedService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly TrackingOptions _options;
    private readonly ILogger<EscalateUnmappedStatusAlertsHostedService> _logger;

    public EscalateUnmappedStatusAlertsHostedService(IServiceScopeFactory scopeFactory, IOptions<TrackingOptions> options, ILogger<EscalateUnmappedStatusAlertsHostedService> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromMinutes(_options.UnmappedStatusEscalationSweepIntervalMinutes);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var sender = scope.ServiceProvider.GetRequiredService<ISender>();
                await sender.Send(new EscalateUnmappedStatusAlertsCommand(), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unmapped-status escalation sweep failed; will retry next interval.");
            }

            await Task.Delay(interval, stoppingToken);
        }
    }
}
