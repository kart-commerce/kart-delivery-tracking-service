using FluentValidation;

namespace KartDeliveryTrackingService.Application.Features.GetTrackingStatus;

public sealed class GetTrackingStatusQueryValidator : AbstractValidator<GetTrackingStatusQuery>
{
    public GetTrackingStatusQueryValidator()
    {
        RuleFor(q => q.TrackingId).NotEmpty();
    }
}
