using KartDeliveryTrackingService.Application.Common.Interfaces;
using KartDeliveryTrackingService.Domain.Tracking;
using KartDeliveryTrackingService.Infrastructure.Persistence.Documents;
using MongoDB.Driver;

namespace KartDeliveryTrackingService.Infrastructure.Persistence;

public sealed class TrackingStatusHistoryRepository : ITrackingStatusHistoryRepository
{
    private readonly MongoContext _context;

    public TrackingStatusHistoryRepository(MongoContext context)
    {
        _context = context;
    }

    public async Task<long> NextSequenceAsync(string trackingId, CancellationToken cancellationToken)
    {
        // database-design.md "Sequence Assignment" - a single-document atomic upsert-and-increment,
        // the same MongoDB-native-atomicity pattern as the ordinal guard, deliberately uncoupled
        // from tracking_records itself.
        var filter = Builders<StatusHistoryCounterDocument>.Filter.Eq(d => d.Id, trackingId);
        var update = Builders<StatusHistoryCounterDocument>.Update.Inc(d => d.NextSequence, 1L);
        var options = new FindOneAndUpdateOptions<StatusHistoryCounterDocument>
        {
            IsUpsert = true,
            ReturnDocument = ReturnDocument.After,
        };

        var counter = await _context.StatusHistoryCounters.FindOneAndUpdateAsync(filter, update, options, cancellationToken);
        return counter.NextSequence;
    }

    public async Task AppendAsync(string deterministicId, StatusHistoryEntry entry, CancellationToken cancellationToken)
    {
        var document = new StatusHistoryEntryDocument
        {
            Id = deterministicId,
            TrackingId = entry.TrackingId,
            Sequence = entry.Sequence,
            CarrierStatusCode = entry.CarrierStatusCode,
            CanonicalStatus = entry.CanonicalStatus?.ToString(),
            IngestionSource = entry.IngestionSource.ToString(),
            IsCarrierSourced = entry.IngestionSource != IngestionSource.System,
            RawPayload = entry.RawPayload,
            EventTimestamp = entry.EventTimestamp.UtcDateTime,
            ReceivedAt = entry.ReceivedAt.UtcDateTime,
            CreatedBy = entry.CreatedBy,
            EscalatedAt = entry.EscalatedAt?.UtcDateTime,
        };

        // Upsert, not insert - see IWebhookDedupRepository's remarks: a redelivered message that
        // reaches this append step twice before its dedup entry is finalized must not produce two
        // rows for the same logical event.
        await _context.StatusHistoryEntries.ReplaceOneAsync(
            d => d.Id == deterministicId,
            document,
            new ReplaceOptions { IsUpsert = true },
            cancellationToken);
    }

    public async Task<IReadOnlyList<StatusHistoryEntry>> GetUntriagedUnmappedAsync(int limit, CancellationToken cancellationToken)
    {
        // Mirrors the partial index's own filter shape exactly (MongoIndexInitializerHostedService)
        // so this query is eligible to use it - equality-only clauses, no $ne/$nin.
        var filter = Builders<StatusHistoryEntryDocument>.Filter.And(
            Builders<StatusHistoryEntryDocument>.Filter.Eq(d => d.CanonicalStatus, null),
            Builders<StatusHistoryEntryDocument>.Filter.Eq(d => d.IsCarrierSourced, true),
            Builders<StatusHistoryEntryDocument>.Filter.Eq(d => d.EscalatedAt, null));

        var documents = await _context.StatusHistoryEntries.Find(filter)
            .SortBy(d => d.ReceivedAt)
            .Limit(limit)
            .ToListAsync(cancellationToken);

        return documents.Select(ToDomain).ToList();
    }

    public async Task MarkEscalatedAsync(string trackingId, long sequence, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var filter = Builders<StatusHistoryEntryDocument>.Filter.And(
            Builders<StatusHistoryEntryDocument>.Filter.Eq(d => d.TrackingId, trackingId),
            Builders<StatusHistoryEntryDocument>.Filter.Eq(d => d.Sequence, sequence));
        var update = Builders<StatusHistoryEntryDocument>.Update.Set(d => d.EscalatedAt, now.UtcDateTime);

        await _context.StatusHistoryEntries.UpdateOneAsync(filter, update, cancellationToken: cancellationToken);
    }

    private static StatusHistoryEntry ToDomain(StatusHistoryEntryDocument document) => new(
        document.TrackingId,
        document.Sequence,
        document.CarrierStatusCode,
        document.CanonicalStatus is null ? null : Enum.Parse<CanonicalDeliveryStatus>(document.CanonicalStatus),
        Enum.Parse<IngestionSource>(document.IngestionSource),
        document.RawPayload,
        new DateTimeOffset(document.EventTimestamp, TimeSpan.Zero),
        new DateTimeOffset(document.ReceivedAt, TimeSpan.Zero),
        document.CreatedBy,
        document.EscalatedAt is null ? null : new DateTimeOffset(document.EscalatedAt.Value, TimeSpan.Zero));
}
