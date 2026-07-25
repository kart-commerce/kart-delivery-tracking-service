using KartDeliveryTrackingService.Domain.Tracking;

namespace KartDeliveryTrackingService.Application.Common.Interfaces;

public interface IWebhookDedupRepository
{
    Task<WebhookDedupEntry?> TryGetAsync(string dedupKey, CancellationToken cancellationToken);

    /// <summary>
    /// Inserted as the LAST step of TRK-4's pipeline (dedup check -> ordinal-guarded update ->
    /// history append -> outbox enqueue -> dedup insert), not the first - this is what makes the
    /// whole pipeline safely re-runnable end to end. If a redelivery of the same
    /// <c>CarrierStatusIngested</c> message reaches this pipeline before this insert ever
    /// succeeded, no dedup entry exists yet, so it correctly re-attempts every step (each of which
    /// is itself idempotent) rather than being silently swallowed by a dedup hit for work that
    /// was never actually completed.
    /// </summary>
    Task InsertAsync(WebhookDedupEntry entry, CancellationToken cancellationToken);

    /// <summary>
    /// TRK-6: retargets every still-live dedup entry for <paramref name="trackingId"/> to
    /// <c>terminalTimestamp + 30 days</c> once that tracking id reaches a terminal status
    /// (edge-cases.md "Duplicate Carrier Webhook Delivery").
    /// </summary>
    Task RetargetExpiryForTrackingIdAsync(string trackingId, DateTimeOffset newExpiresAt, DateTimeOffset now, string updatedBy, CancellationToken cancellationToken);
}
