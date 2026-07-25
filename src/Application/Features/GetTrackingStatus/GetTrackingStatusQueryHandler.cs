using Kart.Shared.Domain;
using KartDeliveryTrackingService.Application.Common.Interfaces;
using KartDeliveryTrackingService.Application.Common.Models;
using MediatR;

namespace KartDeliveryTrackingService.Application.Features.GetTrackingStatus;

public sealed class GetTrackingStatusQueryHandler : IRequestHandler<GetTrackingStatusQuery, Result<TrackingStatusResult>>
{
    private readonly ITrackingRecordRepository _trackingRecords;

    public GetTrackingStatusQueryHandler(ITrackingRecordRepository trackingRecords)
    {
        _trackingRecords = trackingRecords;
    }

    public async Task<Result<TrackingStatusResult>> Handle(GetTrackingStatusQuery request, CancellationToken cancellationToken)
    {
        var record = await _trackingRecords.GetAsync(request.TrackingId, cancellationToken);

        // ddd-model.md Modeling Decision 2: "PENDING" is never persisted - absence of a document
        // IS the pending state, covering both the pre-materialization race and an id this service
        // cannot confirm ever existed. Never a 404 (edge-cases.md "Tracking Query Before the First
        // Status Event Has Arrived").
        if (record is null)
        {
            return Result.Success(TrackingStatusResult.AsPending(request.TrackingId));
        }

        var response = new TrackingResponse(
            record.TrackingId,
            record.CurrentStatus,
            new EtaResponse(record.Eta.Value, record.Eta.Source),
            record.LastUpdatedAt);

        return Result.Success(TrackingStatusResult.Found(response));
    }
}
