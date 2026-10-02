using Norvia.PartnerPortal.Domain.CarrierBookings;

namespace Norvia.PartnerPortal.Domain.UnitTests.CarrierBookings;

public sealed class CarrierAnswerTests
{
    [Fact]
    public void Confirmation_TrimsTheReference()
    {
        new CarrierAnswer.Confirmation("  VDT-1 ").BookingReference.Should().Be("VDT-1");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Confirmation_WithoutAReference_Throws(string? reference)
    {
        var confirm = () => new CarrierAnswer.Confirmation(reference!);

        confirm.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData(CarrierRejectionReason.NoCapacity)]
    [InlineData(CarrierRejectionReason.LaneNotServed)]
    [InlineData(CarrierRejectionReason.WindowNotFeasible)]
    [InlineData(CarrierRejectionReason.GoodsNotAccepted)]
    public void Rejection_ForAReasonOtherThanOther_NeedsNoText(CarrierRejectionReason reason)
    {
        var rejection = new CarrierAnswer.Rejection(reason);

        rejection.Reason.Should().Be(reason);
        rejection.Text.Should().BeNull();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void Rejection_ForOther_NeedsItsText(string? text)
    {
        var reject = () => new CarrierAnswer.Rejection(CarrierRejectionReason.Other, text);

        reject.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Rejection_KeepsATrimmedText_AndReadsBlankTextAsNone()
    {
        new CarrierAnswer.Rejection(CarrierRejectionReason.NoCapacity, "  no trucks ").Text.Should().Be("no trucks");
        new CarrierAnswer.Rejection(CarrierRejectionReason.NoCapacity, "  ").Text.Should().BeNull();
    }
}
