using KartDeliveryTrackingService.Api.Common;
using KartDeliveryTrackingService.Application.Common.Models;
using KartDeliveryTrackingService.Application.Features.GetTrackingStatus;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace KartDeliveryTrackingService.Api.Controllers;

/// <summary>Public, via API Gateway (api-contract.yaml's first server entry).</summary>
[ApiController]
[Route("v1/tracking")]
public sealed class TrackingController : ControllerBase
{
    private const int PendingRetryAfterSeconds = 3;

    private readonly ISender _sender;

    public TrackingController(ISender sender)
    {
        _sender = sender;
    }

    /// <summary>TRK-2: contracts/api-contract.yaml getTrackingStatus - GET /v1/tracking/{trackingId}. Never 404 (ddd-model.md Modeling Decision 2).</summary>
    [HttpGet("{trackingId}")]
    [ProducesResponseType(typeof(TrackingResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(PendingTrackingResponse), StatusCodes.Status202Accepted)]
    public async Task<IActionResult> GetTrackingStatus([FromRoute] string trackingId, CancellationToken cancellationToken)
    {
        var result = await _sender.Send(new GetTrackingStatusQuery(trackingId), cancellationToken);
        if (result.IsFailure)
        {
            return this.MapFailure(result.Error);
        }

        if (result.Value.Tracking is not null)
        {
            return Ok(result.Value.Tracking);
        }

        Response.Headers.RetryAfter = PendingRetryAfterSeconds.ToString();
        return StatusCode(StatusCodes.Status202Accepted, result.Value.Pending);
    }
}
