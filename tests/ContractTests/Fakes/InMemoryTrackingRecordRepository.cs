using System.Collections.Concurrent;
using KartDeliveryTrackingService.Application.Common.Interfaces;
using KartDeliveryTrackingService.Domain.Tracking;

namespace KartDeliveryTrackingService.ContractTests.Fakes;

/// <summary>
/// In-memory fake for HTTP wire-shape contract tests only - no atomicity/ordinal-guard semantics
/// (that's IntegrationTests' job, against a real MongoDB via Testcontainers). Registered as a
/// singleton so seeded data would survive across the single HTTP request each test issues; today
/// no ContractTest seeds a record, so every lookup correctly falls through to
/// GetTrackingStatusQueryHandler's "no document found" branch and the 202-pending contract path.
/// Mirrors kart-inventory-service's InMemoryWarehouseStockRepository shape.
/// </summary>
public sealed class InMemoryTrackingRecordRepository : ITrackingRecordRepository
{
    private readonly ConcurrentDictionary<string, TrackingRecord> _records = new();

    public void Seed(TrackingRecord record) => _records[record.TrackingId] = record;

    public Task<TrackingRecord?> GetAsync(string trackingId, CancellationToken cancellationToken) =>
        Task.FromResult(_records.GetValueOrDefault(trackingId));

    public Task<TrackingRecordCreationResult> CreateIfNotExistsAsync(TrackingRecord record, CancellationToken cancellationToken)
    {
        var wasCreated = _records.TryAdd(record.TrackingId, record);
        var stored = wasCreated ? record : _records[record.TrackingId];
        return Task.FromResult(new TrackingRecordCreationResult(stored, wasCreated));
    }

    public Task<bool> TryApplyOrdinalGuardedUpdateAsync(
        string trackingId,
        CanonicalDeliveryStatus newStatus,
        int incomingOrdinal,
        Eta newEta,
        DateTimeOffset now,
        string updatedBy,
        CancellationToken cancellationToken)
    {
        if (!_records.TryGetValue(trackingId, out var existing))
        {
            return Task.FromResult(false);
        }

        var applied = existing.TryApplyStatusUpdate(newStatus, newEta, now, updatedBy);
        return Task.FromResult(applied);
    }

    public Task<IReadOnlyList<TrackingRecord>> GetStaleNonTerminalAsync(DateTimeOffset staleBefore, int limit, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<TrackingRecord>>(
            _records.Values.Where(r => r.LastUpdatedAt < staleBefore && !r.IsTerminal).Take(limit).ToList());
}
