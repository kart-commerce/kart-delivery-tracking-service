using FluentValidation;

namespace KartDeliveryTrackingService.Application.Features.CreateTrackingRecordOnShipmentDispatched;

public sealed class CreateTrackingRecordOnShipmentDispatchedCommandValidator : AbstractValidator<CreateTrackingRecordOnShipmentDispatchedCommand>
{
    public CreateTrackingRecordOnShipmentDispatchedCommandValidator()
    {
        RuleFor(c => c.OrderId).NotEmpty();
        RuleFor(c => c.Carrier).NotEmpty();
        RuleFor(c => c.TrackingId).NotEmpty();
    }
}
