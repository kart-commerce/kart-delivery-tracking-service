using FluentAssertions;
using KartDeliveryTrackingService.Application.Common.Exceptions;
using KartDeliveryTrackingService.Application.Common.Interfaces;
using KartDeliveryTrackingService.Application.Common.Models;
using KartDeliveryTrackingService.Application.Common.Options;
using KartDeliveryTrackingService.Application.Features.ApplyCarrierStatusUpdate;
using KartDeliveryTrackingService.Domain.Tracking;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace KartDeliveryTrackingService.UnitTests.Application;

public class ApplyCarrierStatusUpdateCommandHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset EventTimestamp = new(2025, 12, 31, 23, 0, 0, TimeSpan.Zero);

    private readonly Mock<ITrackingRecordRepository> _trackingRecords = new();
    private readonly Mock<ITrackingStatusHistoryRepository> _statusHistory = new();
    private readonly Mock<IWebhookDedupRepository> _dedup = new();
    private readonly Mock<IOutboxEventWriter> _outboxEventWriter = new();
    private readonly Mock<ICarrierRegistry> _carrierRegistry = new();

    private ApplyCarrierStatusUpdateCommandHandler CreateHandler() => new(
        _trackingRecords.Object,
        _statusHistory.Object,
        _dedup.Object,
        _outboxEventWriter.Object,
        _carrierRegistry.Object,
        Options.Create(new TrackingOptions()),
        new FakeTimeProvider(Now),
        NullLogger<ApplyCarrierStatusUpdateCommandHandler>.Instance);

    private static TrackingRecord ExistingRecord() =>
        TrackingRecord.Create("trk-1", "order-1", "demo-carrier", new Eta(Now.AddDays(5), EtaSource.SlaFallback), Now.AddHours(-1), "system:test").Value;

    public ApplyCarrierStatusUpdateCommandHandlerTests()
    {
        _statusHistory.Setup(h => h.NextSequenceAsync("trk-1", It.IsAny<CancellationToken>())).ReturnsAsync(2L);
        _carrierRegistry.Setup(r => r.ComputeSlaFallbackEta("demo-carrier", Now)).Returns(Now.AddDays(4));
    }

    [Fact]
    public async Task Handle_WhenDedupKeyAlreadyRecorded_SuppressesAndTouchesNothingElse()
    {
        _carrierRegistry.Setup(r => r.MapStatus("demo-carrier", "IN_TRANSIT")).Returns(CanonicalDeliveryStatus.InTransit);
        _dedup.Setup(d => d.TryGetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WebhookDedupEntry("existing-key", "trk-1", Now.AddMinutes(-1), "system:test", Now.AddDays(7)));

        var command = new ApplyCarrierStatusUpdateCommand("demo-carrier", "trk-1", "IN_TRANSIT", EventTimestamp, "raw", IngestionSource.Webhook);
        var result = await CreateHandler().Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _trackingRecords.Verify(r => r.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        _statusHistory.Verify(h => h.AppendAsync(It.IsAny<string>(), It.IsAny<StatusHistoryEntry>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_WhenTrackingRecordNotYetMaterialized_Throws()
    {
        // requirement-spec.md S4: a tracking record's identity must exist before any carrier
        // status can be recorded against it - thrown so the message consumer's retry ladder
        // treats this as a transient, retryable condition.
        _carrierRegistry.Setup(r => r.MapStatus("demo-carrier", "IN_TRANSIT")).Returns(CanonicalDeliveryStatus.InTransit);
        _dedup.Setup(d => d.TryGetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync((WebhookDedupEntry?)null);
        _trackingRecords.Setup(r => r.GetAsync("trk-1", It.IsAny<CancellationToken>())).ReturnsAsync((TrackingRecord?)null);

        var command = new ApplyCarrierStatusUpdateCommand("demo-carrier", "trk-1", "IN_TRANSIT", EventTimestamp, "raw", IngestionSource.Webhook);

        await Assert.ThrowsAsync<TrackingRecordNotYetMaterializedException>(() => CreateHandler().Handle(command, CancellationToken.None));
    }

    [Fact]
    public async Task Handle_WhenStatusUnmapped_AppendsNullCanonicalHistoryAndFlagsForTriage_NeverAppliesOrdinalUpdate()
    {
        _carrierRegistry.Setup(r => r.MapStatus("demo-carrier", "WEIRD_CODE")).Returns((CanonicalDeliveryStatus?)null);
        _dedup.Setup(d => d.TryGetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync((WebhookDedupEntry?)null);
        _trackingRecords.Setup(r => r.GetAsync("trk-1", It.IsAny<CancellationToken>())).ReturnsAsync(ExistingRecord());

        var command = new ApplyCarrierStatusUpdateCommand("demo-carrier", "trk-1", "WEIRD_CODE", EventTimestamp, "raw", IngestionSource.Webhook);
        var result = await CreateHandler().Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _trackingRecords.Verify(
            r => r.TryApplyOrdinalGuardedUpdateAsync(
                It.IsAny<string>(), It.IsAny<CanonicalDeliveryStatus>(), It.IsAny<int>(), It.IsAny<Eta>(), It.IsAny<DateTimeOffset>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _statusHistory.Verify(
            h => h.AppendAsync(It.IsAny<string>(), It.Is<StatusHistoryEntry>(e => e.CanonicalStatus == null && e.CarrierStatusCode == "WEIRD_CODE"), It.IsAny<CancellationToken>()),
            Times.Once);
        _outboxEventWriter.Verify(
            w => w.EnqueueAsync("UnmappedCarrierStatusFlagged", "trk-1", It.IsAny<UnmappedCarrierStatusFlaggedEventPayload>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()),
            Times.Once);
        _dedup.Verify(d => d.InsertAsync(It.IsAny<WebhookDedupEntry>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_WhenMappedAndOrdinalAdvances_PublishesDeliveryStatusUpdated()
    {
        _carrierRegistry.Setup(r => r.MapStatus("demo-carrier", "IN_TRANSIT")).Returns(CanonicalDeliveryStatus.InTransit);
        _dedup.Setup(d => d.TryGetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync((WebhookDedupEntry?)null);
        _trackingRecords.Setup(r => r.GetAsync("trk-1", It.IsAny<CancellationToken>())).ReturnsAsync(ExistingRecord());
        _trackingRecords
            .Setup(r => r.TryApplyOrdinalGuardedUpdateAsync("trk-1", CanonicalDeliveryStatus.InTransit, 2, It.IsAny<Eta>(), Now, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var command = new ApplyCarrierStatusUpdateCommand("demo-carrier", "trk-1", "IN_TRANSIT", EventTimestamp, "raw", IngestionSource.Webhook);
        var result = await CreateHandler().Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _outboxEventWriter.Verify(
            w => w.EnqueueAsync("DeliveryStatusUpdated", "trk-1", It.Is<DeliveryStatusUpdatedEventPayload>(p => p.Status == "InTransit"), It.IsAny<string?>(), It.IsAny<CancellationToken>()),
            Times.Once);
        _dedup.Verify(d => d.RetargetExpiryForTrackingIdAsync(It.IsAny<string>(), It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_WhenMappedButOrdinalGuardRejects_DoesNotPublish_ButStillAppendsHistoryForAudit()
    {
        _carrierRegistry.Setup(r => r.MapStatus("demo-carrier", "IN_TRANSIT")).Returns(CanonicalDeliveryStatus.InTransit);
        _dedup.Setup(d => d.TryGetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync((WebhookDedupEntry?)null);
        _trackingRecords.Setup(r => r.GetAsync("trk-1", It.IsAny<CancellationToken>())).ReturnsAsync(ExistingRecord());
        _trackingRecords
            .Setup(r => r.TryApplyOrdinalGuardedUpdateAsync(It.IsAny<string>(), It.IsAny<CanonicalDeliveryStatus>(), It.IsAny<int>(), It.IsAny<Eta>(), It.IsAny<DateTimeOffset>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var command = new ApplyCarrierStatusUpdateCommand("demo-carrier", "trk-1", "IN_TRANSIT", EventTimestamp, "raw", IngestionSource.Webhook);
        var result = await CreateHandler().Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _outboxEventWriter.Verify(w => w.EnqueueAsync("DeliveryStatusUpdated", It.IsAny<string>(), It.IsAny<object>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
        _statusHistory.Verify(h => h.AppendAsync(It.IsAny<string>(), It.IsAny<StatusHistoryEntry>(), It.IsAny<CancellationToken>()), Times.Once);
        _dedup.Verify(d => d.InsertAsync(It.IsAny<WebhookDedupEntry>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_WhenTransitionsToTerminalStatus_RetargetsDedupExpiryForTrackingId()
    {
        // TRK-6.
        _carrierRegistry.Setup(r => r.MapStatus("demo-carrier", "DELIVERED")).Returns(CanonicalDeliveryStatus.Delivered);
        _dedup.Setup(d => d.TryGetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync((WebhookDedupEntry?)null);
        _trackingRecords.Setup(r => r.GetAsync("trk-1", It.IsAny<CancellationToken>())).ReturnsAsync(ExistingRecord());
        _trackingRecords
            .Setup(r => r.TryApplyOrdinalGuardedUpdateAsync("trk-1", CanonicalDeliveryStatus.Delivered, 4, It.IsAny<Eta>(), Now, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var command = new ApplyCarrierStatusUpdateCommand("demo-carrier", "trk-1", "DELIVERED", EventTimestamp, "raw", IngestionSource.Webhook);
        var result = await CreateHandler().Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _dedup.Verify(
            d => d.RetargetExpiryForTrackingIdAsync("trk-1", Now.AddDays(new TrackingOptions().DedupEntryTerminalGraceDays), Now, It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }
}
