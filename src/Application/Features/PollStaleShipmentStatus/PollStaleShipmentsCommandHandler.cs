using Kart.Shared.Domain;
using KartDeliveryTrackingService.Application.Common.Interfaces;
using KartDeliveryTrackingService.Application.Common.Options;
using KartDeliveryTrackingService.Application.Features.ApplyCarrierStatusUpdate;
using KartDeliveryTrackingService.Domain.Tracking;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace KartDeliveryTrackingService.Application.Features.PollStaleShipmentStatus;

public sealed class PollStaleShipmentsCommandHandler : IRequestHandler<PollStaleShipmentsCommand, Result<int>>
{
    private readonly ITrackingRecordRepository _trackingRecords;
    private readonly ICarrierTrackingClient _carrierTrackingClient;
    private readonly ISender _sender;
    private readonly TrackingOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<PollStaleShipmentsCommandHandler> _logger;

    public PollStaleShipmentsCommandHandler(
        ITrackingRecordRepository trackingRecords,
        ICarrierTrackingClient carrierTrackingClient,
        ISender sender,
        IOptions<TrackingOptions> options,
        TimeProvider timeProvider,
        ILogger<PollStaleShipmentsCommandHandler> logger)
    {
        _trackingRecords = trackingRecords;
        _carrierTrackingClient = carrierTrackingClient;
        _sender = sender;
        _options = options.Value;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<Result<int>> Handle(PollStaleShipmentsCommand request, CancellationToken cancellationToken)
    {
        var now = _timeProvider.GetUtcNow();
        var staleBefore = now.AddHours(-_options.PollingStalenessThresholdHours);

        var staleRecords = await _trackingRecords.GetStaleNonTerminalAsync(staleBefore, _options.PollingBatchSize, cancellationToken);
        var polled = 0;

        foreach (var record in staleRecords)
        {
            try
            {
                var result = await _carrierTrackingClient.PollAsync(record.CarrierId, record.TrackingId, cancellationToken);
                if (result is null)
                {
                    _logger.LogInformation(
                        "Stage {Stage}: carrier poll returned no result for {TrackingId} ({CarrierId}); skipping.",
                        "CarrierPollNoResultSkipped",
                        record.TrackingId,
                        record.CarrierId);
                    continue;
                }

                var command = new ApplyCarrierStatusUpdateCommand(
                    record.CarrierId,
                    record.TrackingId,
                    result.CarrierStatusCode,
                    result.EventTimestamp,
                    result.RawPayload,
                    IngestionSource.Poll);
                _logger.LogInformation(
                    "Stage {Stage}: dispatching ApplyCarrierStatusUpdateCommand (poll fallback) for {TrackingId}",
                    "ApplyCarrierStatusUpdateCommandDispatched",
                    record.TrackingId);
                await _sender.Send(command, cancellationToken);

                polled++;
            }
            catch (Exception ex)
            {
                // One carrier's failing poll must never abort the sweep for every other stale
                // shipment (design-decisions.md's per-carrier circuit-breaker/bulkhead isolation
                // already bounds a single carrier's own failure impact; this is the sweep-loop
                // level backstop for anything that still escapes it).
                _logger.LogWarning(ex, "Stage {Stage}: polling fallback failed for {TrackingId} ({CarrierId}).", "CarrierPollFailedSkipped", record.TrackingId, record.CarrierId);
            }
        }

        return Result.Success(polled);
    }
}
