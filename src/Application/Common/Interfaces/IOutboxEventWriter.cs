namespace KartDeliveryTrackingService.Application.Common.Interfaces;

/// <summary>
/// This service's own reliable-publish mechanism in place of the EF Core transactional-outbox
/// pattern other Kart services use (there is no PostgreSQL <c>DbContext</c> here to hook a
/// <c>SaveChanges</c> interceptor into - architecture.md's Boundary Rationale). A row written here
/// is relayed onto <c>tracking.exchange</c> by the Infrastructure-layer outbox relay, which
/// retries indefinitely until RabbitMQ is reachable rather than dead-lettering - the publish-side
/// half of "at-least-once delivery" (ddd-model.md's per-aggregate domain-event invariants).
/// </summary>
public interface IOutboxEventWriter
{
    /// <summary>
    /// Enqueues one event for relay. When <paramref name="deterministicId"/> is supplied, this is
    /// an idempotent upsert (never a second row for the same id) - required wherever the calling
    /// pipeline step could itself be re-run by a message redelivery (TRK-4's
    /// <c>DeliveryStatusUpdated</c>/<c>UnmappedCarrierStatusFlagged</c> publishes). Left null where
    /// a harmless duplicate event is an accepted, already-designed-for outcome (TRK-3's
    /// <c>CarrierStatusIngested</c> - a genuine carrier webhook retry producing two copies is
    /// caught downstream by TRK-4's own dedup check, so no idempotency is needed at this layer).
    /// </summary>
    Task EnqueueAsync(string eventType, string aggregateId, object payload, string? deterministicId, CancellationToken cancellationToken);
}
