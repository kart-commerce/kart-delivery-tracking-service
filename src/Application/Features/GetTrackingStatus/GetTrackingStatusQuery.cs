using Kart.Shared.Domain;
using KartDeliveryTrackingService.Application.Common.Models;
using MediatR;

namespace KartDeliveryTrackingService.Application.Features.GetTrackingStatus;

/// <summary>TRK-2: <c>GET /v1/tracking/{trackingId}</c> (api-contract.yaml).</summary>
public sealed record GetTrackingStatusQuery(string TrackingId) : IRequest<Result<TrackingStatusResult>>;
