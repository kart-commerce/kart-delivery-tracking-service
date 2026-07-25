using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using KartDeliveryTrackingService.Application.Common.Interfaces;
using KartDeliveryTrackingService.Infrastructure.Carriers;
using Moq;
using Xunit;

namespace KartDeliveryTrackingService.UnitTests.Infrastructure;

public class HmacCarrierWebhookVerifierTests
{
    private readonly Mock<ICarrierRegistry> _carrierRegistry = new();

    private HmacCarrierWebhookVerifier CreateVerifier() => new(_carrierRegistry.Object);

    [Fact]
    public void Verify_WithCorrectSignature_ReturnsTrue()
    {
        const string secret = "shh";
        _carrierRegistry.Setup(r => r.TryGetWebhookSharedSecret("demo-carrier", out It.Ref<string>.IsAny))
            .Returns((string _, out string s) =>
            {
                s = secret;
                return true;
            });

        var body = Encoding.UTF8.GetBytes("{\"trackingId\":\"trk-1\"}");
        var signature = Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), body));

        CreateVerifier().Verify("demo-carrier", body, signature).Should().BeTrue();
    }

    [Fact]
    public void Verify_WithIncorrectSignature_ReturnsFalse()
    {
        _carrierRegistry.Setup(r => r.TryGetWebhookSharedSecret("demo-carrier", out It.Ref<string>.IsAny))
            .Returns((string _, out string s) =>
            {
                s = "shh";
                return true;
            });

        CreateVerifier().Verify("demo-carrier", Encoding.UTF8.GetBytes("{}"), "deadbeef").Should().BeFalse();
    }

    [Fact]
    public void Verify_WithMissingSignatureHeader_ReturnsFalse()
    {
        CreateVerifier().Verify("demo-carrier", Encoding.UTF8.GetBytes("{}"), null).Should().BeFalse();
    }

    [Fact]
    public void Verify_ForUnconfiguredCarrier_ReturnsFalse()
    {
        _carrierRegistry.Setup(r => r.TryGetWebhookSharedSecret("unknown", out It.Ref<string>.IsAny))
            .Returns((string _, out string s) =>
            {
                s = string.Empty;
                return false;
            });

        CreateVerifier().Verify("unknown", Encoding.UTF8.GetBytes("{}"), "anything").Should().BeFalse();
    }
}
