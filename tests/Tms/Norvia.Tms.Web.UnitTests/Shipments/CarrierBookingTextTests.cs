using Norvia.Tms.Application.CarrierBookings;
using Norvia.Tms.Application.Shipments.ListToRelease;
using Norvia.Tms.Domain.CarrierBookings;
using Norvia.Tms.Web.Shipments;
using Norvia.Tms.Web.UnitTests.TransportOrders;

namespace Norvia.Tms.Web.UnitTests.Shipments;

public sealed class CarrierBookingTextTests
{
    private static readonly DateTimeOffset Moment = new(2026, 9, 24, 9, 45, 0, TimeSpan.Zero);

    [Fact]
    public void LastOutcome_Rejected_NamesTheCarrierAndTheirReasonText()
    {
        var outcome = new LastCarrierBookingOutcome(CarrierBookingStatus.Rejected, Samples.Kessler, CarrierBookingRejectionReason.NoCapacity, "Fully booked until Friday.");

        CarrierBookingText.LastOutcome(outcome).Should().Be("Rejected by Kessler Spedition: Fully booked until Friday.");
    }

    [Theory]
    [InlineData(CarrierBookingRejectionReason.NoCapacity, "No capacity")]
    [InlineData(CarrierBookingRejectionReason.LaneNotServed, "Lane not served")]
    [InlineData(CarrierBookingRejectionReason.WindowNotFeasible, "Window not feasible")]
    [InlineData(CarrierBookingRejectionReason.GoodsNotAccepted, "Goods not accepted")]
    [InlineData(CarrierBookingRejectionReason.Other, "No reason given")]
    public void LastOutcome_RejectedWithoutText_ReadsTheReasonCode(CarrierBookingRejectionReason code, string expected)
    {
        var outcome = new LastCarrierBookingOutcome(CarrierBookingStatus.Rejected, Samples.Kessler, code, null);

        CarrierBookingText.LastOutcome(outcome).Should().Be($"Rejected by Kessler Spedition: {expected}");
    }

    [Fact]
    public void LastOutcome_NotDelivered_NamesTheCarrier()
    {
        var outcome = new LastCarrierBookingOutcome(CarrierBookingStatus.NotDelivered, Samples.Kessler, null, null);

        CarrierBookingText.LastOutcome(outcome).Should().Be("Not delivered to Kessler Spedition");
    }

    [Fact]
    public void LastOutcome_Withdrawn_NamesTheCarrier()
    {
        var outcome = new LastCarrierBookingOutcome(CarrierBookingStatus.Withdrawn, Samples.Kessler, null, null);

        CarrierBookingText.LastOutcome(outcome).Should().Be("Withdrawn from Kessler Spedition");
    }

    [Fact]
    public void LastOutcome_Cancelled_SaysNorviaCancelledItAndGivesTheReason()
    {
        var outcome = new LastCarrierBookingOutcome(CarrierBookingStatus.Cancelled, Samples.Kessler, null, "Carrier won't make its collection");

        CarrierBookingText.LastOutcome(outcome).Should().Be("Cancelled by Norvia: Carrier won't make its collection");
    }

    [Fact]
    public void Detail_Confirmed_GivesTheCarrierBookingReference()
    {
        CarrierBookingText.Detail(Booking(CarrierBookingStatus.Confirmed) with { CarrierBookingReference = "KS-7781" })
            .Should().Be("Carrier booking reference KS-7781");
    }

    [Fact]
    public void Detail_Rejected_GivesTheReason()
    {
        CarrierBookingText.Detail(Booking(CarrierBookingStatus.Rejected) with { ReasonCode = CarrierBookingRejectionReason.NoCapacity })
            .Should().Be("No capacity");
    }

    [Fact]
    public void Detail_NotDelivered_GivesTheTechnicalCause()
    {
        CarrierBookingText.Detail(Booking(CarrierBookingStatus.NotDelivered) with { NotDeliveredMessage = "The Carrier's API timed out." })
            .Should().Be("The Carrier's API timed out.");
    }

    [Fact]
    public void Detail_Withdrawn_SaysWhen()
    {
        CarrierBookingText.Detail(Booking(CarrierBookingStatus.Withdrawn) with { WithdrawnAt = Moment })
            .Should().Be("Withdrawn Thu 24 Sep 2026, 11:45");
    }

    [Fact]
    public void Detail_Requested_HasNone() =>
        CarrierBookingText.Detail(Booking(CarrierBookingStatus.Requested)).Should().BeEmpty();

    [Fact]
    public void Sent_BeforeTheCarrierAcknowledged_SaysNotYetSent() => CarrierBookingText.Sent(null).Should().Be("Not yet sent");

    [Fact]
    public void Sent_Acknowledged_GivesTheTimeInRotterdam() => CarrierBookingText.Sent(Moment).Should().Be("Thu 24 Sep 2026, 11:45");

    private static CarrierBookingView Booking(CarrierBookingStatus status) =>
        new("CB-0000001", Samples.Kessler, status, Moment, null, null, null, null, null, null, null, null, null);
}
