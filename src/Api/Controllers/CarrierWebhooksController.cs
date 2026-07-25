using KartDeliveryTrackingService.Application.Features.IngestCarrierWebhook;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace KartDeliveryTrackingService.Api.Controllers;

/// <summary>contracts/api-contract.yaml's <c>ErrorResponse</c> schema - deliberately not the platform's ProblemDetails envelope: this endpoint's callers are external carrier webhook senders, not Kart-internal clients.</summary>
public sealed record ErrorResponse(string Code, string Message, DateTimeOffset Timestamp);

/// <summary>Internal network segment only (api-contract.yaml's second server entry) - reachable by per-carrier webhook senders, external third parties, not Kart peers.</summary>
[ApiController]
[Route("internal/v1/webhooks/carriers")]
public sealed class CarrierWebhooksController : ControllerBase
{
    private readonly ISender _sender;
    private readonly TimeProvider _timeProvider;

    public CarrierWebhooksController(ISender sender, TimeProvider timeProvider)
    {
        _sender = sender;
        _timeProvider = timeProvider;
    }

    /// <summary>TRK-3: contracts/api-contract.yaml ingestCarrierWebhook - POST /internal/v1/webhooks/carriers/{carrierId}.</summary>
    [HttpPost("{carrierId}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> IngestCarrierWebhook([FromRoute] string carrierId, CancellationToken cancellationToken)
    {
        Request.EnableBuffering();
        using var reader = new MemoryStream();
        await Request.Body.CopyToAsync(reader, cancellationToken);
        var rawBody = reader.ToArray();

        var signatureHeader = Request.Headers["X-Carrier-Signature"].FirstOrDefault();
        var result = await _sender.Send(new IngestCarrierWebhookCommand(carrierId, signatureHeader, rawBody), cancellationToken);

        if (result.IsSuccess)
        {
            return Ok();
        }

        var statusCode = result.Error.Code switch
        {
            "unauthorized_signature" => StatusCodes.Status401Unauthorized,
            "carrier_not_configured" => StatusCodes.Status404NotFound,
            "enqueue_failed" => StatusCodes.Status503ServiceUnavailable,
            _ => StatusCodes.Status400BadRequest,
        };

        var response = new ErrorResponse(result.Error.Code.ToUpperInvariant(), result.Error.Message, _timeProvider.GetUtcNow());
        return StatusCode(statusCode, response);
    }
}
