using Kart.Shared.Domain;
using MediatR;

namespace KartDeliveryTrackingService.Application.Features.CreateTrackingRecordOnShipmentDispatched;

/// <summary>
/// TRK-1: consumes <c>ShipmentDispatched</c> (event-contract.md) - the aggregate-creation trigger
/// for this service's <c>TrackingRecord</c> (ddd-model.md).
/// </summary>
public sealed record CreateTrackingRecordOnShipmentDispatchedCommand(string OrderId, string Carrier, string TrackingId) : IRequest<Result>;
