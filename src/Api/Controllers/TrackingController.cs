using Kart.Shared.Observability;
using KartDeliveryTrackingService.Api.Common;
using KartDeliveryTrackingService.Application.Common;
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
    private readonly ILogger<TrackingController> _logger;

    public TrackingController(ISender sender, ILogger<TrackingController> logger)
    {
        _sender = sender;
        _logger = logger;
    }

    /// <summary>TRK-2: contracts/api-contract.yaml getTrackingStatus - GET /v1/tracking/{trackingId}. Never 404 (ddd-model.md Modeling Decision 2).</summary>
    [HttpGet("{trackingId}")]
    [ProducesResponseType(typeof(TrackingResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(PendingTrackingResponse), StatusCodes.Status202Accepted)]
    public async Task<IActionResult> GetTrackingStatus([FromRoute] string trackingId, CancellationToken cancellationToken)
    {
        // business-flows.md flow #14, "Customer Support & Order Tracking" - this endpoint is the
        // order-tracking/status-lookup step; see FlowNames.CustomerSupportOrderTracking's remarks.
        using var _ = KartFlowContext.Push(FlowNames.CustomerSupportOrderTracking);
        _logger.LogInformation("Stage {Stage}: get-tracking-status request received for {TrackingId}", "GetTrackingStatusRequestReceived", trackingId);

        var query = new GetTrackingStatusQuery(trackingId);
        var result = await _sender.Send(query, cancellationToken);
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
