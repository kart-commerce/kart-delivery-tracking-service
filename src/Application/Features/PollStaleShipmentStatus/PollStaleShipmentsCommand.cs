using Kart.Shared.Domain;
using MediatR;

namespace KartDeliveryTrackingService.Application.Features.PollStaleShipmentStatus;

/// <summary>
/// TRK-5: edge-cases.md "Carrier Webhook Failure or Silence" - the scheduled polling-fallback
/// sweep, triggered per-shipment once its <c>lastUpdatedAt</c> exceeds the configured staleness
/// threshold. Feeds every polled result into TRK-4's same <c>ApplyCarrierStatusUpdate</c> pipeline,
/// tagged <see cref="Domain.Tracking.IngestionSource.Poll"/>.
/// </summary>
public sealed record PollStaleShipmentsCommand : IRequest<Result<int>>;
