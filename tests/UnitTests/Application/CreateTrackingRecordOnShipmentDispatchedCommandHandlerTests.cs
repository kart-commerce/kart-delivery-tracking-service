using FluentAssertions;
using KartDeliveryTrackingService.Application.Common.Interfaces;
using KartDeliveryTrackingService.Application.Features.CreateTrackingRecordOnShipmentDispatched;
using KartDeliveryTrackingService.Domain.Tracking;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace KartDeliveryTrackingService.UnitTests.Application;

public class CreateTrackingRecordOnShipmentDispatchedCommandHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private readonly Mock<ITrackingRecordRepository> _trackingRecords = new();
    private readonly Mock<ITrackingStatusHistoryRepository> _statusHistory = new();
    private readonly Mock<ICarrierRegistry> _carrierRegistry = new();

    private CreateTrackingRecordOnShipmentDispatchedCommandHandler CreateHandler() => new(
        _trackingRecords.Object,
        _statusHistory.Object,
        _carrierRegistry.Object,
        new FakeTimeProvider(Now),
        NullLogger<CreateTrackingRecordOnShipmentDispatchedCommandHandler>.Instance);

    public CreateTrackingRecordOnShipmentDispatchedCommandHandlerTests()
    {
        _carrierRegistry.Setup(r => r.ComputeSlaFallbackEta("demo-carrier", Now)).Returns(Now.AddDays(5));
    }

    [Fact]
    public async Task Handle_WhenRecordDoesNotYetExist_CreatesRecordAndSystemAnchorHistoryEntry()
    {
        _trackingRecords
            .Setup(r => r.CreateIfNotExistsAsync(It.IsAny<TrackingRecord>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((TrackingRecord record, CancellationToken _) => new TrackingRecordCreationResult(record, WasCreated: true));
        _statusHistory.Setup(h => h.NextSequenceAsync("trk-1", It.IsAny<CancellationToken>())).ReturnsAsync(1L);

        var handler = CreateHandler();
        var result = await handler.Handle(new CreateTrackingRecordOnShipmentDispatchedCommand("order-1", "demo-carrier", "trk-1"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _statusHistory.Verify(
            h => h.AppendAsync(
                "trk-1:system-anchor",
                It.Is<StatusHistoryEntry>(e => e.IngestionSource == IngestionSource.System && e.CanonicalStatus == CanonicalDeliveryStatus.Dispatched),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_WhenRecordAlreadyExists_IsIdempotent_AndDoesNotAppendAnotherAnchor()
    {
        // Redelivery of the same ShipmentDispatched message must not duplicate the SYSTEM
        // history anchor or fail.
        var existing = TrackingRecord.Create("trk-1", "order-1", "demo-carrier", new Eta(Now.AddDays(5), EtaSource.SlaFallback), Now, "system:test").Value;
        _trackingRecords
            .Setup(r => r.CreateIfNotExistsAsync(It.IsAny<TrackingRecord>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TrackingRecordCreationResult(existing, WasCreated: false));

        var handler = CreateHandler();
        var result = await handler.Handle(new CreateTrackingRecordOnShipmentDispatchedCommand("order-1", "demo-carrier", "trk-1"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _statusHistory.Verify(h => h.AppendAsync(It.IsAny<string>(), It.IsAny<StatusHistoryEntry>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
