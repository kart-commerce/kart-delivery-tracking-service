using FluentAssertions;
using KartDeliveryTrackingService.Application.Common.Interfaces;
using KartDeliveryTrackingService.Application.Features.EscalateUnmappedStatusAlerts;
using KartDeliveryTrackingService.Domain.Tracking;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace KartDeliveryTrackingService.UnitTests.Application;

public class EscalateUnmappedStatusAlertsCommandHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 10, 0, 0, 0, TimeSpan.Zero);

    private readonly Mock<ITrackingStatusHistoryRepository> _statusHistory = new();
    private readonly Mock<ITrackingRecordRepository> _trackingRecords = new();

    private EscalateUnmappedStatusAlertsCommandHandler CreateHandler() => new(
        _statusHistory.Object,
        _trackingRecords.Object,
        new FakeTimeProvider(Now),
        NullLogger<EscalateUnmappedStatusAlertsCommandHandler>.Instance);

    private static StatusHistoryEntry UnmappedEntry(string trackingId, long sequence) => new(
        trackingId, sequence, "WEIRD_CODE", null, IngestionSource.Webhook, "raw", Now.AddDays(-2), Now.AddDays(-2), "system:test");

    [Fact]
    public async Task Handle_WhenEtaWindowHasPassed_EscalatesAndMarksEntry()
    {
        _statusHistory
            .Setup(h => h.GetUntriagedUnmappedAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<StatusHistoryEntry> { UnmappedEntry("trk-1", 2) });
        var record = TrackingRecord.Create("trk-1", "order-1", "demo-carrier", new Eta(Now.AddDays(-1), EtaSource.SlaFallback), Now.AddDays(-5), "system:test").Value;
        _trackingRecords.Setup(r => r.GetAsync("trk-1", It.IsAny<CancellationToken>())).ReturnsAsync(record);

        var result = await CreateHandler().Handle(new EscalateUnmappedStatusAlertsCommand(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(1);
        _statusHistory.Verify(h => h.MarkEscalatedAsync("trk-1", 2, Now, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_WhenEtaWindowHasNotYetPassed_DoesNotEscalate()
    {
        _statusHistory
            .Setup(h => h.GetUntriagedUnmappedAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<StatusHistoryEntry> { UnmappedEntry("trk-1", 2) });
        var record = TrackingRecord.Create("trk-1", "order-1", "demo-carrier", new Eta(Now.AddDays(3), EtaSource.SlaFallback), Now.AddDays(-5), "system:test").Value;
        _trackingRecords.Setup(r => r.GetAsync("trk-1", It.IsAny<CancellationToken>())).ReturnsAsync(record);

        var result = await CreateHandler().Handle(new EscalateUnmappedStatusAlertsCommand(), CancellationToken.None);

        result.Value.Should().Be(0);
        _statusHistory.Verify(h => h.MarkEscalatedAsync(It.IsAny<string>(), It.IsAny<long>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
