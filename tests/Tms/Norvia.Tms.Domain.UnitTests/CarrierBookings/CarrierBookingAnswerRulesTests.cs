using Norvia.Tms.Domain.CarrierBookings;
using Norvia.Tms.Domain.Carriers;
using Norvia.Tms.Domain.Shipments;
using Norvia.Tms.Domain.TransportOrders;

namespace Norvia.Tms.Domain.UnitTests.CarrierBookings;

/// <summary>P4's answer rules: what the TMS refuses before it records the Carrier's answer.</summary>
public sealed class CarrierBookingAnswerRulesTests
{
    private static readonly CarrierId VanDijk = new("CAR-0001");
    private static readonly CorrelationId Release = new("corr-release");
    private static readonly DateTimeOffset RequestedAt = new(2026, 9, 24, 12, 45, 0, TimeSpan.Zero);
    private static readonly ShipmentId Shipment = ShipmentId.New();
    private static readonly ShipmentNumber ShipmentNumberOfTwelve = ShipmentNumber.FromSequence(12);

    [Fact]
    public void BookingNotFound_NamesTheNumberTheCallerGave()
    {
        var refusal = CarrierBookingAnswerRules.BookingNotFound("CB-9999999");

        refusal.Code.Should().Be(CarrierBookingRefusalCode.BookingNotFound);
        refusal.Message.Should().Be("Carrier booking CB-9999999 is not known to the TMS.");
    }

    [Fact]
    public void CheckCarrier_TheBookingsOwnCarrier_RefusesNothing()
    {
        CarrierBookingAnswerRules.CheckCarrier(Requested(), VanDijk).Should().BeNull();
    }

    [Fact]
    public void CheckCarrier_AnotherCarrier_IsRefused_WithNoStableCode()
    {
        var refusal = CarrierBookingAnswerRules.CheckCarrier(Requested(), new CarrierId("CAR-9999"));

        refusal!.Code.Should().BeNull("the design's answer rules give the wrong-Carrier rule no code, only \"Refused\"");
        refusal.Message.Should().Be("Carrier booking CB-0000042 belongs to Carrier CAR-0001, not CAR-9999.");
    }

    [Fact]
    public void NotOpen_NamesTheBookingsCurrentStatus()
    {
        var booking = Requested();
        booking.Reject(CarrierBookingRejectionReason.NoCapacity, reasonText: null, new CorrelationId("corr-answer"), RequestedAt.AddHours(1));

        var refusal = CarrierBookingAnswerRules.NotOpen(booking);

        refusal.Code.Should().Be(CarrierBookingRefusalCode.BookingNotOpen);
        refusal.Message.Should().Be("Carrier booking CB-0000042 is Rejected, not Requested.");
    }

    private static CarrierBooking Requested() =>
        CarrierBooking.Request(CarrierBookingNumber.FromSequence(42), Shipment, ShipmentNumberOfTwelve, VanDijk, Release, RequestedAt);
}
