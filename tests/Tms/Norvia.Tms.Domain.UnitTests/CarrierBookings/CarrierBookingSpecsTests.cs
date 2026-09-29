using Norvia.Tms.Domain.CarrierBookings;
using Norvia.Tms.Domain.Carriers;
using Norvia.Tms.Domain.Shipments;
using Norvia.Tms.Domain.TransportOrders;

namespace Norvia.Tms.Domain.UnitTests.CarrierBookings;

public sealed class CarrierBookingSpecsTests
{
    private static readonly CarrierId VanDijk = new("CAR-0001");
    private static readonly DateTimeOffset RequestedAt = new(2026, 9, 24, 12, 45, 0, TimeSpan.Zero);

    [Fact]
    public void CarrierBookingsForShipmentsSpec_FindsOnlyBookingsForThoseShipments_MostRecentlyRequestedFirst()
    {
        var first = Booking(ShipmentId.New(), ShipmentNumber.FromSequence(1), 1, RequestedAt);
        var second = Booking(ShipmentId.New(), ShipmentNumber.FromSequence(2), 2, RequestedAt.AddHours(1));
        var third = Booking(ShipmentId.New(), ShipmentNumber.FromSequence(3), 3, RequestedAt.AddHours(2));

        var found = new CarrierBookingsForShipmentsSpec([first.ShipmentId, third.ShipmentId]).Evaluate([first, second, third]);

        found.Select(booking => booking.Number).Should().Equal(third.Number, first.Number);
    }

    [Fact]
    public void RequestedCarrierBookingsSpec_FindsOnlyTheRequestedBookings()
    {
        var requested = Booking(ShipmentId.New(), ShipmentNumber.FromSequence(1), 1, RequestedAt);
        var withdrawn = Booking(ShipmentId.New(), ShipmentNumber.FromSequence(2), 2, RequestedAt);
        withdrawn.Withdraw(new IdempotencyKey("key"), new CorrelationId("corr-withdraw"), RequestedAt.AddHours(1));

        var found = new RequestedCarrierBookingsSpec().Evaluate([requested, withdrawn]);

        found.Should().ContainSingle().Which.Should().BeSameAs(requested);
    }

    private static CarrierBooking Booking(ShipmentId shipmentId, ShipmentNumber shipmentNumber, long sequence, DateTimeOffset requestedAt) =>
        CarrierBooking.Request(
            CarrierBookingNumber.FromSequence(sequence), shipmentId, shipmentNumber, VanDijk, new CorrelationId($"corr-release-{sequence}"), requestedAt);
}
