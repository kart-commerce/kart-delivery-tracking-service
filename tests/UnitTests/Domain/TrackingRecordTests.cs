using FluentAssertions;
using KartDeliveryTrackingService.Domain.Tracking;
using Xunit;

namespace KartDeliveryTrackingService.UnitTests.Domain;

public class TrackingRecordTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly Eta InitialEta = new(Now.AddDays(5), EtaSource.SlaFallback);

    [Fact]
    public void Create_WithValidInputs_StartsAtDispatchedOrdinalOne()
    {
        var result = TrackingRecord.Create("trk-1", "order-1", "demo-carrier", InitialEta, Now, "system:shipment-dispatched-consumer");

        result.IsSuccess.Should().BeTrue();
        result.Value.CurrentStatus.Should().Be(CanonicalDeliveryStatus.Dispatched);
        result.Value.CurrentOrdinal.Should().Be(1);
        result.Value.IsTerminal.Should().BeFalse();
    }

    [Theory]
    [InlineData("", "order-1", "demo-carrier")]
    [InlineData("trk-1", "", "demo-carrier")]
    [InlineData("trk-1", "order-1", "")]
    public void Create_WithMissingRequiredField_FailsValidation(string trackingId, string orderId, string carrierId)
    {
        var result = TrackingRecord.Create(trackingId, orderId, carrierId, InitialEta, Now, "system:test");

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("validation_error");
    }

    [Fact]
    public void TryApplyStatusUpdate_WhenOrdinalStrictlyIncreases_AppliesAndReturnsTrue()
    {
        var record = TrackingRecord.Create("trk-1", "order-1", "demo-carrier", InitialEta, Now, "system:test").Value;
        var newEta = new Eta(Now.AddDays(3), EtaSource.SlaFallback);

        var applied = record.TryApplyStatusUpdate(CanonicalDeliveryStatus.InTransit, newEta, Now.AddHours(1), "system:carrier-webhook:demo-carrier");

        applied.Should().BeTrue();
        record.CurrentStatus.Should().Be(CanonicalDeliveryStatus.InTransit);
        record.CurrentOrdinal.Should().Be(2);
        record.Eta.Should().Be(newEta);
    }

    [Fact]
    public void TryApplyStatusUpdate_WhenOrdinalDoesNotIncrease_RejectsAndLeavesStateUnchanged()
    {
        // edge-cases.md "Out-of-Order Carrier Status Updates" - a later-arriving webhook
        // reporting an earlier lifecycle stage must never regress the exposed status.
        var record = TrackingRecord.Create("trk-1", "order-1", "demo-carrier", InitialEta, Now, "system:test").Value;
        record.TryApplyStatusUpdate(CanonicalDeliveryStatus.OutForDelivery, InitialEta, Now.AddHours(1), "system:test");

        var applied = record.TryApplyStatusUpdate(CanonicalDeliveryStatus.InTransit, InitialEta, Now.AddHours(2), "system:test");

        applied.Should().BeFalse();
        record.CurrentStatus.Should().Be(CanonicalDeliveryStatus.OutForDelivery);
        record.CurrentOrdinal.Should().Be(3);
    }

    [Fact]
    public void TryApplyStatusUpdate_WhenAlreadyTerminal_RejectsAnotherTerminalValue()
    {
        // database-design.md's "Ordinal Assignment": all three terminal values share ordinal 4 -
        // once terminal, the guard rejects every subsequent update, including a different
        // terminal value (e.g. a stray Returned webhook arriving after Delivered).
        var record = TrackingRecord.Create("trk-1", "order-1", "demo-carrier", InitialEta, Now, "system:test").Value;
        record.TryApplyStatusUpdate(CanonicalDeliveryStatus.Delivered, InitialEta, Now.AddDays(1), "system:test");

        var applied = record.TryApplyStatusUpdate(CanonicalDeliveryStatus.Returned, InitialEta, Now.AddDays(2), "system:test");

        applied.Should().BeFalse();
        record.CurrentStatus.Should().Be(CanonicalDeliveryStatus.Delivered);
        record.IsTerminal.Should().BeTrue();
    }
}
