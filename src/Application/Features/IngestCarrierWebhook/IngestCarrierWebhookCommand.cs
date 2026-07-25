using Kart.Shared.Domain;
using MediatR;

namespace KartDeliveryTrackingService.Application.Features.IngestCarrierWebhook;

/// <summary>
/// TRK-3: <c>POST /internal/v1/webhooks/carriers/{carrierId}</c> (api-contract.yaml). Durably
/// buffers a carrier's native webhook payload for asynchronous processing
/// (design-decisions.md "Durable Ingestion Buffering Pattern") - this handler never touches
/// dedup/ordinal/mapping itself (TRK-4's job).
/// </summary>
public sealed record IngestCarrierWebhookCommand(string CarrierId, string? SignatureHeader, byte[] RawBody) : IRequest<Result>;
