using Kart.Shared.Domain;
using Kart.Shared.ErrorHandling;
using Microsoft.AspNetCore.Mvc;

namespace KartDeliveryTrackingService.Api.Common;

/// <summary>
/// Translates a Handler's <see cref="Result"/> failure (api-standards.md: "Domain/business errors
/// use a Result/Either pattern - not exceptions") into the platform's consistent
/// <see cref="Kart.Shared.ErrorHandling.KartProblemDetailsFactory"/> envelope - the same shape
/// <see cref="Kart.Shared.ErrorHandling.KartExceptionHandler"/> uses for unhandled exceptions, so
/// every error response from this service, expected or not, looks identical to a caller.
/// </summary>
public static class ResultExtensions
{
    public static ActionResult MapFailure(this ControllerBase controller, Error error)
    {
        var statusCode = error.Code switch
        {
            "validation_error" => StatusCodes.Status400BadRequest,
            "unauthorized_signature" => StatusCodes.Status401Unauthorized,
            "not_found" or "carrier_not_configured" => StatusCodes.Status404NotFound,
            "enqueue_failed" => StatusCodes.Status503ServiceUnavailable,
            _ => StatusCodes.Status500InternalServerError,
        };

        var problem = KartProblemDetailsFactory.Create(controller.HttpContext, statusCode, error.Code, error.Message);
        return controller.StatusCode(statusCode, problem);
    }
}
