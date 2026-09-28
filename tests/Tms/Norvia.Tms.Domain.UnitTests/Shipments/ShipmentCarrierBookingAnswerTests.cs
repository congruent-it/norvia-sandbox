using Norvia.Tms.Domain.CarrierBookings;
using Norvia.Tms.Domain.Carriers;
using Norvia.Tms.Domain.Shipments;
using Norvia.Tms.Domain.Shippers;
using Norvia.Tms.Domain.TransportOrders;
using Norvia.Tms.Domain.UnitTests.TransportOrders;

namespace Norvia.Tms.Domain.UnitTests.Shipments;

/// <summary>
/// A Released Carrier Shipment moves on once its Carrier booking is answered (P4): Booked on confirmation, or back to
/// Planned on any other end. Neither move raises a domain event of its own; the Carrier booking's own event already
/// says so.
/// </summary>
public sealed class ShipmentCarrierBookingAnswerTests
{
    private static readonly DateTimeOffset ReceivedAt = new(2026, 9, 24, 10, 15, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset PlannedAt = ReceivedAt.AddHours(2);
    private static readonly DateTimeOffset ReleasedAt = PlannedAt.AddMinutes(30);

    [Fact]
    public void Book_AReleasedShipment_MakesItBooked_AndRaisesNoDomainEvent()
    {
        var shipment = Released();

        var booked = shipment.Book();

        booked.Should().BeTrue();
        shipment.Status.Should().Be(ShipmentStatus.Booked);
        shipment.DomainEvents.Should().BeEmpty();
    }

    [Fact]
    public void Book_APlannedShipment_ChangesNothing()
    {
        var (_, shipment) = Planned();

        var booked = shipment.Book();

        booked.Should().BeFalse();
        shipment.Status.Should().Be(ShipmentStatus.Planned);
    }

    [Fact]
    public void ReturnToPlanned_AReleasedShipment_MakesItPlannedAgain_AndRaisesNoDomainEvent()
    {
        var shipment = Released();

        var returned = shipment.ReturnToPlanned();

        returned.Should().BeTrue();
        shipment.Status.Should().Be(ShipmentStatus.Planned);
        shipment.DomainEvents.Should().BeEmpty();
    }

    [Fact]
    public void ReturnToPlanned_APlannedShipment_ChangesNothing()
    {
        var (_, shipment) = Planned();

        var returned = shipment.ReturnToPlanned();

        returned.Should().BeFalse();
        shipment.Status.Should().Be(ShipmentStatus.Planned);
    }

    private static Shipment Released()
    {
        var (order, shipment) = Planned();
        shipment.Release(order, new IdempotencyKey("key-rel1"), new CorrelationId("corr-release"), ReleasedAt, CarrierBookingNumber.FromSequence(42));
        shipment.ClearDomainEvents();
        return shipment;
    }

    private static (TransportOrder Order, Shipment Shipment) Planned()
    {
        var order = TransportOrder.Accept(
            new ShipperId("SHP-0001"),
            TransportOrderTests.SampleDetails(),
            TransportOrderNumber.FromSequence(7),
            IntakeChannel.ShipperApi,
            new CorrelationId("corr-intake"),
            ReceivedAt);
        var carriedBy = CarriedBy.Carrier(new CarrierId("CAR-0001"));
        var shipment = order.Plan(ShipmentNumber.FromSequence(3), carriedBy, new IdempotencyKey("key-1"), new CorrelationId("corr-planning"), PlannedAt)!;
        shipment.ClearDomainEvents();
        return (order, shipment);
    }
}
