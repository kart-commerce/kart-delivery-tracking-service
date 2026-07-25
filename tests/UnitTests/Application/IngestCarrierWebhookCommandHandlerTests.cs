using System.Text;
using FluentAssertions;
using KartDeliveryTrackingService.Application.Common.Interfaces;
using KartDeliveryTrackingService.Application.Common.Models;
using KartDeliveryTrackingService.Application.Features.IngestCarrierWebhook;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace KartDeliveryTrackingService.UnitTests.Application;

public class IngestCarrierWebhookCommandHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private readonly Mock<ICarrierRegistry> _carrierRegistry = new();
    private readonly Mock<ICarrierWebhookVerifier> _verifier = new();
    private readonly Mock<IOutboxEventWriter> _outboxEventWriter = new();

    private IngestCarrierWebhookCommandHandler CreateHandler() => new(
        _carrierRegistry.Object,
        _verifier.Object,
        _outboxEventWriter.Object,
        new FakeTimeProvider(Now),
        NullLogger<IngestCarrierWebhookCommandHandler>.Instance);

    private static byte[] Body(string json) => Encoding.UTF8.GetBytes(json);

    [Fact]
    public async Task Handle_WhenCarrierNotConfigured_FailsWithCarrierNotConfigured_AndNeverVerifies()
    {
        _carrierRegistry.Setup(r => r.IsConfigured("unknown")).Returns(false);

        var result = await CreateHandler().Handle(new IngestCarrierWebhookCommand("unknown", "sig", Body("{}")), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("carrier_not_configured");
        _verifier.Verify(v => v.Verify(It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<string?>()), Times.Never);
    }

    [Fact]
    public async Task Handle_WhenSignatureInvalid_FailsWithUnauthorizedSignature_AndNeverEnqueues()
    {
        _carrierRegistry.Setup(r => r.IsConfigured("demo-carrier")).Returns(true);
        _verifier.Setup(v => v.Verify("demo-carrier", It.IsAny<byte[]>(), "bad-sig")).Returns(false);

        var result = await CreateHandler().Handle(new IngestCarrierWebhookCommand("demo-carrier", "bad-sig", Body("{}")), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("unauthorized_signature");
        _outboxEventWriter.Verify(w => w.EnqueueAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<object>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_WhenValid_EnqueuesCarrierStatusIngestedWithNormalizedPayload()
    {
        _carrierRegistry.Setup(r => r.IsConfigured("demo-carrier")).Returns(true);
        _verifier.Setup(v => v.Verify("demo-carrier", It.IsAny<byte[]>(), "good-sig")).Returns(true);

        var body = Body("""{"trackingId":"trk-1","statusCode":"PICKED_UP","eventTimestamp":"2026-01-01T00:00:00Z"}""");
        var result = await CreateHandler().Handle(new IngestCarrierWebhookCommand("demo-carrier", "good-sig", body), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _outboxEventWriter.Verify(
            w => w.EnqueueAsync(
                "CarrierStatusIngested",
                "trk-1",
                It.Is<CarrierStatusIngestedEventPayload>(p => p.TrackingId == "trk-1" && p.CarrierStatusCode == "PICKED_UP" && p.CarrierId == "demo-carrier"),
                null,
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_WhenBodyMissingRequiredFields_FailsValidation()
    {
        _carrierRegistry.Setup(r => r.IsConfigured("demo-carrier")).Returns(true);
        _verifier.Setup(v => v.Verify("demo-carrier", It.IsAny<byte[]>(), "good-sig")).Returns(true);

        var result = await CreateHandler().Handle(new IngestCarrierWebhookCommand("demo-carrier", "good-sig", Body("{}")), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("validation_error");
    }
}
