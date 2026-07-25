using KartDeliveryTrackingService.Application.Common.Interfaces;
using KartDeliveryTrackingService.Domain.Tracking;
using KartDeliveryTrackingService.Infrastructure.Persistence.Documents;
using MongoDB.Driver;

namespace KartDeliveryTrackingService.Infrastructure.Persistence;

public sealed class TrackingRecordRepository : ITrackingRecordRepository
{
    private readonly MongoContext _context;

    public TrackingRecordRepository(MongoContext context)
    {
        _context = context;
    }

    public async Task<TrackingRecord?> GetAsync(string trackingId, CancellationToken cancellationToken)
    {
        var document = await _context.TrackingRecords
            .Find(d => d.Id == trackingId)
            .FirstOrDefaultAsync(cancellationToken);

        return document is null ? null : ToDomain(document);
    }

    public async Task<TrackingRecordCreationResult> CreateIfNotExistsAsync(TrackingRecord record, CancellationToken cancellationToken)
    {
        try
        {
            await _context.TrackingRecords.InsertOneAsync(ToDocument(record), cancellationToken: cancellationToken);
            return new TrackingRecordCreationResult(record, WasCreated: true);
        }
        catch (MongoWriteException ex) when (ex.WriteError.Category == ServerErrorCategory.DuplicateKey)
        {
            var existing = await GetAsync(record.TrackingId, cancellationToken)
                ?? throw new InvalidOperationException($"Duplicate-key insert for '{record.TrackingId}' but no document found on re-read.");
            return new TrackingRecordCreationResult(existing, WasCreated: false);
        }
    }

    public async Task<bool> TryApplyOrdinalGuardedUpdateAsync(
        string trackingId,
        CanonicalDeliveryStatus newStatus,
        int incomingOrdinal,
        Eta newEta,
        DateTimeOffset now,
        string updatedBy,
        CancellationToken cancellationToken)
    {
        // design-decisions.md "Concurrency Control for the Status-Regression Guard" - a single
        // atomic round trip, relying on MongoDB's native single-document atomicity. No lock
        // service, no read-then-write retry loop.
        var filter = Builders<TrackingRecordDocument>.Filter.And(
            Builders<TrackingRecordDocument>.Filter.Eq(d => d.Id, trackingId),
            Builders<TrackingRecordDocument>.Filter.Lt(d => d.CurrentOrdinal, incomingOrdinal));

        var update = Builders<TrackingRecordDocument>.Update
            .Set(d => d.CurrentStatus, newStatus.ToString())
            .Set(d => d.CurrentOrdinal, incomingOrdinal)
            .Set(d => d.Eta, new EtaDocument { Value = newEta.Value.UtcDateTime, Source = newEta.Source.ToString() })
            .Set(d => d.LastUpdatedAt, now.UtcDateTime)
            .Set(d => d.UpdatedBy, updatedBy);

        var result = await _context.TrackingRecords.UpdateOneAsync(filter, update, cancellationToken: cancellationToken);
        return result.ModifiedCount > 0;
    }

    public async Task<IReadOnlyList<TrackingRecord>> GetStaleNonTerminalAsync(DateTimeOffset staleBefore, int limit, CancellationToken cancellationToken)
    {
        // Matches MongoIndexInitializerHostedService's partial-index filter shape exactly (ordinal,
        // not status string - partial-filter expressions don't support $nin).
        var filter = Builders<TrackingRecordDocument>.Filter.And(
            Builders<TrackingRecordDocument>.Filter.Lt(d => d.LastUpdatedAt, staleBefore.UtcDateTime),
            Builders<TrackingRecordDocument>.Filter.Lt(d => d.CurrentOrdinal, LifecycleOrdinal.MaxOrdinal));

        var documents = await _context.TrackingRecords.Find(filter).Limit(limit).ToListAsync(cancellationToken);
        return documents.Select(ToDomain).ToList();
    }

    private static TrackingRecordDocument ToDocument(TrackingRecord record) => new()
    {
        Id = record.TrackingId,
        OrderId = record.OrderId,
        CarrierId = record.CarrierId,
        CurrentStatus = record.CurrentStatus.ToString(),
        CurrentOrdinal = record.CurrentOrdinal,
        Eta = new EtaDocument { Value = record.Eta.Value.UtcDateTime, Source = record.Eta.Source.ToString() },
        LastUpdatedAt = record.LastUpdatedAt.UtcDateTime,
        CreatedAt = record.CreatedAt.UtcDateTime,
        CreatedBy = record.CreatedBy,
        UpdatedBy = record.UpdatedBy,
    };

    private static TrackingRecord ToDomain(TrackingRecordDocument document) => TrackingRecord.Rehydrate(
        document.Id,
        document.OrderId,
        document.CarrierId,
        Enum.Parse<CanonicalDeliveryStatus>(document.CurrentStatus),
        document.CurrentOrdinal,
        new Eta(new DateTimeOffset(document.Eta.Value, TimeSpan.Zero), Enum.Parse<EtaSource>(document.Eta.Source)),
        new DateTimeOffset(document.LastUpdatedAt, TimeSpan.Zero),
        new DateTimeOffset(document.CreatedAt, TimeSpan.Zero),
        document.CreatedBy,
        document.UpdatedBy);
}
