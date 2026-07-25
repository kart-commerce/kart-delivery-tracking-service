using Kart.Shared.Domain;
using KartDeliveryTrackingService.Domain.Tracking;
using MediatR;

namespace KartDeliveryTrackingService.Application.Features.ApplyCarrierStatusUpdate;

/// <summary>
/// TRK-4: this service's highest-value and highest-risk slice (tickets.md) - dedup check,
/// ordinal-guarded conditional update, unconditional history append, and the two possible
/// downstream publishes (ddd-model.md's Cross-Aggregate Interaction). Driven both by the
/// <c>CarrierStatusIngested</c> consumer (<see cref="IngestionSource.Webhook"/>) and TRK-5's
/// polling fallback (<see cref="IngestionSource.Poll"/>).
/// </summary>
public sealed record ApplyCarrierStatusUpdateCommand(
    string CarrierId,
    string TrackingId,
    string CarrierStatusCode,
    DateTimeOffset EventTimestamp,
    string? RawPayload,
    IngestionSource IngestionSource) : IRequest<Result>;
