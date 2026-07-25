namespace KartDeliveryTrackingService.Domain.Tracking;

/// <summary>
/// ddd-model.md's <c>WebhookDedupEntry</c> aggregate - a purely internal idempotency ledger, one
/// entry per distinct received carrier-status update, keyed by <see cref="DedupKey"/>. Backed by
/// MongoDB's <c>webhook_dedup_entries</c> collection with a TTL index on <see cref="ExpiresAt"/>
/// (design-decisions.md "Idempotency Mechanism Design").
/// </summary>
public sealed class WebhookDedupEntry
{
    public string DedupKey { get; }

    public string TrackingId { get; }

    public DateTimeOffset CreatedAt { get; }

    public string CreatedBy { get; }

    public DateTimeOffset ExpiresAt { get; private set; }

    public DateTimeOffset? UpdatedAt { get; private set; }

    public string? UpdatedBy { get; private set; }

    public WebhookDedupEntry(
        string dedupKey,
        string trackingId,
        DateTimeOffset createdAt,
        string createdBy,
        DateTimeOffset expiresAt,
        DateTimeOffset? updatedAt = null,
        string? updatedBy = null)
    {
        DedupKey = dedupKey;
        TrackingId = trackingId;
        CreatedAt = createdAt;
        CreatedBy = createdBy;
        ExpiresAt = expiresAt;
        UpdatedAt = updatedAt;
        UpdatedBy = updatedBy;
    }

    /// <summary>
    /// edge-cases.md "Duplicate Carrier Webhook Delivery": once a <see cref="TrackingId"/> reaches
    /// a terminal status, every one of its still-live dedup entries is retargeted to
    /// <c>terminalTimestamp + 30 days</c> (TRK-6) instead of the 7-day baseline - never extended,
    /// only shortened.
    /// </summary>
    public void RetargetExpiryForTerminalStatus(DateTimeOffset earlierExpiresAt, DateTimeOffset now, string updatedBy)
    {
        if (earlierExpiresAt < ExpiresAt)
        {
            ExpiresAt = earlierExpiresAt;
            UpdatedAt = now;
            UpdatedBy = updatedBy;
        }
    }
}
