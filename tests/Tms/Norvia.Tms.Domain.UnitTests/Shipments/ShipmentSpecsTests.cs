using Norvia.Tms.Domain.Shipments;
using Norvia.Tms.Domain.Shippers;
using Norvia.Tms.Domain.TransportOrders;
using Norvia.Tms.Domain.UnitTests.TransportOrders;

namespace Norvia.Tms.Domain.UnitTests.Shipments;

public sealed class ShipmentSpecsTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 24, 10, 15, 0, TimeSpan.Zero);

    [Fact]
    public void ShipmentByNumberSpec_FindsOnlyTheShipmentWithThatNumber()
    {
        Shipment[] shipments = [Planned(1, 1), Planned(2, 2)];

        var found = new ShipmentByNumberSpec(ShipmentNumber.FromSequence(2)).Evaluate(shipments);

        found.Should().ContainSingle().Which.Number.Should().Be(ShipmentNumber.FromSequence(2));
    }

    [Fact]
    public void ShipmentToChangeSpec_FindsOnlyTheShipmentWithThatNumber()
    {
        Shipment[] shipments = [Planned(1, 1), Planned(2, 2)];

        var found = new ShipmentToChangeSpec(ShipmentNumber.FromSequence(2)).Evaluate(shipments);

        found.Should().ContainSingle().Which.Number.Should().Be(ShipmentNumber.FromSequence(2));
    }

    [Fact]
    public void ShipmentsToReleaseSpec_FindsThePlannedShipments_InTheOrderTheyWerePlanned()
    {
        var first = Planned(1, 1);
        var released = Accepted(2);
        var releasedShipment = released.Plan(
            ShipmentNumber.FromSequence(2), CarriedBy.OwnFleet, new IdempotencyKey("key"), new CorrelationId("corr"), Now.AddMinutes(1))!;
        releasedShipment.Release(released, new IdempotencyKey("key-rel"), new CorrelationId("corr-rel"), Now.AddMinutes(2));
        var last = Accepted(3).Plan(
            ShipmentNumber.FromSequence(3), CarriedBy.OwnFleet, new IdempotencyKey("key"), new CorrelationId("corr"), Now.AddMinutes(3))!;

        var found = new ShipmentsToReleaseSpec().Evaluate([last, releasedShipment, first]);

        found.Select(shipment => shipment.Number).Should().Equal(first.Number, last.Number);
    }

    [Fact]
    public void TransportOrderToPlanSpec_FindsOnlyTheTransportOrderWithThatNumber()
    {
        TransportOrder[] orders = [Accepted(1), Accepted(2)];

        var found = new TransportOrderToPlanSpec(TransportOrderNumber.FromSequence(2)).Evaluate(orders);

        found.Should().ContainSingle().Which.Number.Should().Be(TransportOrderNumber.FromSequence(2));
    }

    [Fact]
    public void ShipmentsByIdsSpec_FindsOnlyTheShipmentsWithThoseIds()
    {
        var first = Planned(1, 1);
        var second = Planned(2, 2);
        var third = Planned(3, 3);

        var found = new ShipmentsByIdsSpec([first.Id, third.Id]).Evaluate([first, second, third]);

        found.Select(shipment => shipment.Number).Should().BeEquivalentTo([first.Number, third.Number]);
    }

    [Fact]
    public void ShipmentsByNumbersSpec_FindsOnlyTheShipmentsWithThoseNumbers()
    {
        var first = Planned(1, 1);
        var second = Planned(2, 2);
        var third = Planned(3, 3);

        var found = new ShipmentsByNumbersSpec([first.Number, third.Number]).Evaluate([first, second, third]);

        found.Select(shipment => shipment.Number).Should().BeEquivalentTo([first.Number, third.Number]);
    }

    [Fact]
    public void ReleasedOwnFleetShipmentsSpec_FindsTheReleasedOwnFleetShipments_InTheOrderTheyWerePlanned()
    {
        var planned = Planned(1, 1);
        var later = Released(2, 2, CarriedBy.OwnFleet, minutes: 5);
        var earlier = Released(3, 3, CarriedBy.OwnFleet, minutes: 1);
        var carrier = Released(4, 4, CarriedBy.Carrier(new Norvia.Tms.Domain.Carriers.CarrierId("CAR-0001")), minutes: 2, bookingNumber: Norvia.Tms.Domain.CarrierBookings.CarrierBookingNumber.FromSequence(1));

        var found = new ReleasedOwnFleetShipmentsSpec().Evaluate([planned, later, carrier, earlier]);

        found.Select(shipment => shipment.Number).Should().Equal(earlier.Number, later.Number);
    }

    private static Shipment Released(long orderSequence, long shipmentSequence, CarriedBy carriedBy, int minutes, Norvia.Tms.Domain.CarrierBookings.CarrierBookingNumber? bookingNumber = null)
    {
        var order = Accepted(orderSequence);
        var shipment = order.Plan(ShipmentNumber.FromSequence(shipmentSequence), carriedBy, new IdempotencyKey("key"), new CorrelationId("corr"), Now.AddMinutes(minutes))!;
        shipment.Release(order, new IdempotencyKey("key-rel"), new CorrelationId("corr-rel"), Now.AddMinutes(minutes + 1), bookingNumber);
        return shipment;
    }

    private static Shipment Planned(long orderSequence, long shipmentSequence) =>
        Accepted(orderSequence).Plan(
            ShipmentNumber.FromSequence(shipmentSequence), CarriedBy.OwnFleet, new IdempotencyKey("key"), new CorrelationId("corr"), Now)!;

    private static TransportOrder Accepted(long sequence) =>
        TransportOrder.Accept(
            new ShipperId("SHP-0001"),
            TransportOrderTests.SampleDetails(),
            TransportOrderNumber.FromSequence(sequence),
            IntakeChannel.ShipperApi,
            new CorrelationId("corr-intake"),
            Now);
}
