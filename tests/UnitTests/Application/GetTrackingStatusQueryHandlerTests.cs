using FluentAssertions;
using KartDeliveryTrackingService.Application.Common.Interfaces;
using KartDeliveryTrackingService.Application.Features.GetTrackingStatus;
using KartDeliveryTrackingService.Domain.Tracking;
using Moq;
using Xunit;

namespace KartDeliveryTrackingService.UnitTests.Application;

public class GetTrackingStatusQueryHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private readonly Mock<ITrackingRecordRepository> _trackingRecords = new();

    private GetTrackingStatusQueryHandler CreateHandler() => new(_trackingRecords.Object);

    [Fact]
    public async Task Handle_WhenNoRecordExists_ReturnsPending_NeverAFailure()
    {
        // ddd-model.md Modeling Decision 2 / edge-cases.md "Tracking Query Before the First
        // Status Event Has Arrived" - absence of a document IS the pending state, never a 404.
        _trackingRecords.Setup(r => r.GetAsync("trk-unknown", It.IsAny<CancellationToken>())).ReturnsAsync((TrackingRecord?)null);

        var result = await CreateHandler().Handle(new GetTrackingStatusQuery("trk-unknown"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Tracking.Should().BeNull();
        result.Value.Pending.Should().NotBeNull();
        result.Value.Pending!.TrackingId.Should().Be("trk-unknown");
    }

    [Fact]
    public async Task Handle_WhenRecordExists_ReturnsFoundWithCurrentStatusAndEta()
    {
        var record = TrackingRecord.Create("trk-1", "order-1", "demo-carrier", new Eta(Now.AddDays(5), EtaSource.SlaFallback), Now, "system:test").Value;
        _trackingRecords.Setup(r => r.GetAsync("trk-1", It.IsAny<CancellationToken>())).ReturnsAsync(record);

        var result = await CreateHandler().Handle(new GetTrackingStatusQuery("trk-1"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Pending.Should().BeNull();
        result.Value.Tracking.Should().NotBeNull();
        result.Value.Tracking!.Status.Should().Be(CanonicalDeliveryStatus.Dispatched);
        result.Value.Tracking.TrackingId.Should().Be("trk-1");
    }
}
