using Kart.Shared.Domain;
using MediatR;

namespace KartDeliveryTrackingService.Application.Features.EscalateUnmappedStatusAlerts;

/// <summary>
/// TRK-7: edge-cases.md "Unmapped Carrier Status" - an untriaged unmapped-status entry is a
/// routine (non-paging) operational ticket by default (surfaced immediately via
/// <c>UnmappedCarrierStatusFlagged</c>, TRK-4); this sweep escalates it to a paged, same-day
/// priority once its shipment's ETA window passes while still unmapped.
/// </summary>
public sealed record EscalateUnmappedStatusAlertsCommand : IRequest<Result<int>>;
