using Norvia.Tms.Domain.Shipments;
using Norvia.Tms.Domain.Shippers;
using Norvia.Tms.Domain.ShipperTracking;
using Norvia.Tms.Domain.TransportOrders;
using Norvia.Tms.Domain.UnitTests.TransportOrders;

namespace Norvia.Tms.Domain.UnitTests.ShipperTracking;

public sealed class ShipperTransportOrdersSpecTests
{
    private static readonly DateTimeOffset Morning = new(2026, 9, 24, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Evaluated_ListsOnlyTheShippersAcceptedAndPlannedTransportOrders()
    {
        var accepted = Accepted("SHP-0001", 1);
        var planned = Accepted("SHP-0001", 2);
        planned.Plan(ShipmentNumber.FromSequence(2), CarriedBy.OwnFleet, new IdempotencyKey("key-1"), new CorrelationId("corr-plan"), Morning);
        var rejected = TransportOrder.Reject(
            new ShipperId("SHP-0001"), TransportOrderTests.SampleDetails(), [new RejectionReason(RejectionCode.GoodsLinesMissing, "None.")],
            IntakeChannel.ShipperApi, new CorrelationId("corr-rejected"), Morning);
        var anotherShippers = Accepted("SHP-0003", 3);

        var listed = new ShipperTransportOrdersSpec(new ShipperId("SHP-0001")).Evaluate([accepted, planned, rejected, anotherShippers]);

        listed.Should().BeEquivalentTo([accepted, planned]);
    }

    [Fact]
    public void ShipmentsOfTransportOrders_Evaluated_FindsOnlyTheShipmentsOfThoseOrders()
    {
        var one = Accepted("SHP-0001", 1);
        var two = Accepted("SHP-0001", 2);
        var shipmentOfOne = one.Plan(ShipmentNumber.FromSequence(1), CarriedBy.OwnFleet, new IdempotencyKey("key-1"), new CorrelationId("corr-1"), Morning)!;
        var shipmentOfTwo = two.Plan(ShipmentNumber.FromSequence(2), CarriedBy.OwnFleet, new IdempotencyKey("key-2"), new CorrelationId("corr-2"), Morning)!;

        var found = new ShipmentsOfTransportOrdersSpec([one.Id]).Evaluate([shipmentOfOne, shipmentOfTwo]);

        found.Should().ContainSingle().Which.TransportOrderId.Should().Be(one.Id);
    }

    private static TransportOrder Accepted(string shipperId, long sequence) => TransportOrder.Accept(
        new ShipperId(shipperId),
        TransportOrderTests.SampleDetails(),
        TransportOrderNumber.FromSequence(sequence),
        IntakeChannel.ShipperApi,
        new CorrelationId($"corr-{sequence}"),
        Morning);
}
