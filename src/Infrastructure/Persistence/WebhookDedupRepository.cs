using KartDeliveryTrackingService.Application.Common.Interfaces;
using KartDeliveryTrackingService.Domain.Tracking;
using KartDeliveryTrackingService.Infrastructure.Persistence.Documents;
using MongoDB.Driver;

namespace KartDeliveryTrackingService.Infrastructure.Persistence;

public sealed class WebhookDedupRepository : IWebhookDedupRepository
{
    private readonly MongoContext _context;

    public WebhookDedupRepository(MongoContext context)
    {
        _context = context;
    }

    public async Task<WebhookDedupEntry?> TryGetAsync(string dedupKey, CancellationToken cancellationToken)
    {
        var document = await _context.WebhookDedupEntries.Find(d => d.Id == dedupKey).FirstOrDefaultAsync(cancellationToken);
        return document is null ? null : ToDomain(document);
    }

    public async Task InsertAsync(WebhookDedupEntry entry, CancellationToken cancellationToken)
    {
        var document = new WebhookDedupEntryDocument
        {
            Id = entry.DedupKey,
            TrackingId = entry.TrackingId,
            CreatedAt = entry.CreatedAt.UtcDateTime,
            CreatedBy = entry.CreatedBy,
            ExpiresAt = entry.ExpiresAt.UtcDateTime,
            UpdatedAt = entry.UpdatedAt?.UtcDateTime,
            UpdatedBy = entry.UpdatedBy,
        };

        // Upsert (never a plain insert): this is TRK-4's final pipeline step, and the same
        // redelivery-safety reasoning as the history append applies - this exact update may reach
        // this step more than once before RabbitMQ sees the ack.
        await _context.WebhookDedupEntries.ReplaceOneAsync(
            d => d.Id == entry.DedupKey,
            document,
            new ReplaceOptions { IsUpsert = true },
            cancellationToken);
    }

    public async Task RetargetExpiryForTrackingIdAsync(string trackingId, DateTimeOffset newExpiresAt, DateTimeOffset now, string updatedBy, CancellationToken cancellationToken)
    {
        // TRK-6: only shortens - never extends - a still-live entry's expiry (edge-cases.md
        // "Duplicate Carrier Webhook Delivery").
        var filter = Builders<WebhookDedupEntryDocument>.Filter.And(
            Builders<WebhookDedupEntryDocument>.Filter.Eq(d => d.TrackingId, trackingId),
            Builders<WebhookDedupEntryDocument>.Filter.Gt(d => d.ExpiresAt, newExpiresAt.UtcDateTime));

        var update = Builders<WebhookDedupEntryDocument>.Update
            .Set(d => d.ExpiresAt, newExpiresAt.UtcDateTime)
            .Set(d => d.UpdatedAt, now.UtcDateTime)
            .Set(d => d.UpdatedBy, updatedBy);

        await _context.WebhookDedupEntries.UpdateManyAsync(filter, update, cancellationToken: cancellationToken);
    }

    private static WebhookDedupEntry ToDomain(WebhookDedupEntryDocument document) => new(
        document.Id,
        document.TrackingId,
        new DateTimeOffset(document.CreatedAt, TimeSpan.Zero),
        document.CreatedBy,
        new DateTimeOffset(document.ExpiresAt, TimeSpan.Zero),
        document.UpdatedAt is null ? null : new DateTimeOffset(document.UpdatedAt.Value, TimeSpan.Zero),
        document.UpdatedBy);
}
