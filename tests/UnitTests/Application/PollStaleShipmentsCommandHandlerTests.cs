using FluentAssertions;
using KartDeliveryTrackingService.Application.Common.Interfaces;
using KartDeliveryTrackingService.Application.Common.Options;
using KartDeliveryTrackingService.Application.Features.ApplyCarrierStatusUpdate;
using KartDeliveryTrackingService.Application.Features.PollStaleShipmentStatus;
using KartDeliveryTrackingService.Domain.Tracking;
using MediatR;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace KartDeliveryTrackingService.UnitTests.Application;

public class PollStaleShipmentsCommandHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private readonly Mock<ITrackingRecordRepository> _trackingRecords = new();
    private readonly Mock<ICarrierTrackingClient> _carrierTrackingClient = new();
    private readonly Mock<ISender> _sender = new();

    private PollStaleShipmentsCommandHandler CreateHandler() => new(
        _trackingRecords.Object,
        _carrierTrackingClient.Object,
        _sender.Object,
        Options.Create(new TrackingOptions()),
        new FakeTimeProvider(Now),
        NullLogger<PollStaleShipmentsCommandHandler>.Instance);

    private static TrackingRecord StaleRecord(string trackingId) =>
        TrackingRecord.Create(trackingId, "order-1", "demo-carrier", new Eta(Now.AddDays(5), EtaSource.SlaFallback), Now.AddHours(-10), "system:test").Value;

    [Fact]
    public async Task Handle_ForEachStaleShipmentWithAPollResult_DispatchesApplyCarrierStatusUpdateTaggedPoll()
    {
        var stale = StaleRecord("trk-1");
        _trackingRecords
            .Setup(r => r.GetStaleNonTerminalAsync(It.IsAny<DateTimeOffset>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<TrackingRecord> { stale });
        _carrierTrackingClient
            .Setup(c => c.PollAsync("demo-carrier", "trk-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CarrierPollResult("IN_TRANSIT", Now.AddHours(-1), "raw"));

        var result = await CreateHandler().Handle(new PollStaleShipmentsCommand(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(1);
        _sender.Verify(
            s => s.Send(
                It.Is<ApplyCarrierStatusUpdateCommand>(c => c.TrackingId == "trk-1" && c.IngestionSource == IngestionSource.Poll && c.CarrierStatusCode == "IN_TRANSIT"),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_WhenCarrierReturnsNoResult_SkipsWithoutDispatching()
    {
        var stale = StaleRecord("trk-1");
        _trackingRecords
            .Setup(r => r.GetStaleNonTerminalAsync(It.IsAny<DateTimeOffset>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<TrackingRecord> { stale });
        _carrierTrackingClient.Setup(c => c.PollAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync((CarrierPollResult?)null);

        var result = await CreateHandler().Handle(new PollStaleShipmentsCommand(), CancellationToken.None);

        result.Value.Should().Be(0);
        _sender.Verify(s => s.Send(It.IsAny<ApplyCarrierStatusUpdateCommand>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_WhenOneShipmentPollThrows_ContinuesProcessingTheRest()
    {
        var first = StaleRecord("trk-1");
        var second = StaleRecord("trk-2");
        _trackingRecords
            .Setup(r => r.GetStaleNonTerminalAsync(It.IsAny<DateTimeOffset>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<TrackingRecord> { first, second });
        _carrierTrackingClient.Setup(c => c.PollAsync("demo-carrier", "trk-1", It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException("boom"));
        _carrierTrackingClient
            .Setup(c => c.PollAsync("demo-carrier", "trk-2", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CarrierPollResult("IN_TRANSIT", Now, "raw"));

        var result = await CreateHandler().Handle(new PollStaleShipmentsCommand(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(1);
    }
}
