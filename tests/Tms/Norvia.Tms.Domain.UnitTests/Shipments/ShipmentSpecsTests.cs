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
    public void ShipmentToReassignSpec_FindsOnlyTheShipmentWithThatNumber()
    {
        Shipment[] shipments = [Planned(1, 1), Planned(2, 2)];

        var found = new ShipmentToReassignSpec(ShipmentNumber.FromSequence(2)).Evaluate(shipments);

        found.Should().ContainSingle().Which.Number.Should().Be(ShipmentNumber.FromSequence(2));
    }

    [Fact]
    public void TransportOrderToPlanSpec_FindsOnlyTheTransportOrderWithThatNumber()
    {
        TransportOrder[] orders = [Accepted(1), Accepted(2)];

        var found = new TransportOrderToPlanSpec(TransportOrderNumber.FromSequence(2)).Evaluate(orders);

        found.Should().ContainSingle().Which.Number.Should().Be(TransportOrderNumber.FromSequence(2));
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
