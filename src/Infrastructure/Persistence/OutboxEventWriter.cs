using System.Text.Json;
using KartDeliveryTrackingService.Application.Common.Interfaces;
using KartDeliveryTrackingService.Infrastructure.Persistence.Documents;
using MongoDB.Driver;

namespace KartDeliveryTrackingService.Infrastructure.Persistence;

public sealed class OutboxEventWriter : IOutboxEventWriter
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private readonly MongoContext _context;
    private readonly TimeProvider _timeProvider;

    public OutboxEventWriter(MongoContext context, TimeProvider timeProvider)
    {
        _context = context;
        _timeProvider = timeProvider;
    }

    public async Task EnqueueAsync(string eventType, string aggregateId, object payload, string? deterministicId, CancellationToken cancellationToken)
    {
        var id = deterministicId ?? Guid.NewGuid().ToString("N");
        var payloadJson = JsonSerializer.Serialize(payload, SerializerOptions);
        var occurredAt = _timeProvider.GetUtcNow().UtcDateTime;

        if (deterministicId is null)
        {
            await _context.OutboxEvents.InsertOneAsync(
                new TrackingOutboxEventDocument
                {
                    Id = id,
                    AggregateId = aggregateId,
                    EventType = eventType,
                    Payload = payloadJson,
                    OccurredAt = occurredAt,
                    PublishedAt = null,
                },
                cancellationToken: cancellationToken);
            return;
        }

        // Idempotent upsert keyed on deterministicId (see IOutboxEventWriter's remarks). Uses
        // $setOnInsert rather than a full replace so a re-run of the calling pipeline step never
        // clobbers PublishedAt back to null on a row the relay has already published - it can
        // only ever create the row, never resurrect an already-relayed one.
        var filter = Builders<TrackingOutboxEventDocument>.Filter.Eq(d => d.Id, id);
        var update = Builders<TrackingOutboxEventDocument>.Update
            .SetOnInsert(d => d.AggregateId, aggregateId)
            .SetOnInsert(d => d.EventType, eventType)
            .SetOnInsert(d => d.Payload, payloadJson)
            .SetOnInsert(d => d.OccurredAt, occurredAt);

        await _context.OutboxEvents.UpdateOneAsync(filter, update, new UpdateOptions { IsUpsert = true }, cancellationToken);
    }
}
