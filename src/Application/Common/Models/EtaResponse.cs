using KartDeliveryTrackingService.Domain.Tracking;

namespace KartDeliveryTrackingService.Application.Common.Models;

/// <summary>api-contract.yaml's <c>Eta</c> schema.</summary>
public sealed record EtaResponse(DateTimeOffset Value, EtaSource Source);
