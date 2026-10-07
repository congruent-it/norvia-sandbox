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

    [Fact]
    public void ByNumber_Evaluated_FindsTheShippersOrderAndNotAnotherShippersWithTheSameNumber()
    {
        var own = Accepted("SHP-0001", 1);
        var anothers = Accepted("SHP-0003", 1);

        new ShipperTransportOrderByNumberSpec(new ShipperId("SHP-0001"), TransportOrderNumber.FromSequence(1)).Evaluate([anothers, own])
            .Should().ContainSingle().Which.Should().BeSameAs(own);
        new ShipperTransportOrderByNumberSpec(new ShipperId("SHP-0001"), TransportOrderNumber.FromSequence(2)).Evaluate([own, anothers])
            .Should().BeEmpty();
    }

    [Fact]
    public void ByReference_Evaluated_FindsTheShippersOrderByItsReference_NotARejectedOneOrAnotherShippers()
    {
        var own = Accepted("SHP-0001", 1);
        var anothers = Accepted("SHP-0003", 2);
        var rejected = TransportOrder.Reject(
            new ShipperId("SHP-0001"), TransportOrderTests.SampleDetails(), [new RejectionReason(RejectionCode.GoodsLinesMissing, "None.")],
            IntakeChannel.ShipperApi, new CorrelationId("corr-rejected"), Morning);
        var reference = own.ShipperReference!;

        new ShipperTransportOrderByReferenceSpec(new ShipperId("SHP-0001"), reference).Evaluate([anothers, rejected, own])
            .Should().ContainSingle().Which.Should().BeSameAs(own);
        new ShipperTransportOrderByReferenceSpec(new ShipperId("SHP-0001"), "other").Evaluate([own]).Should().BeEmpty();
    }

    private static TransportOrder Accepted(string shipperId, long sequence) => TransportOrder.Accept(
        new ShipperId(shipperId),
        TransportOrderTests.SampleDetails(),
        TransportOrderNumber.FromSequence(sequence),
        IntakeChannel.ShipperApi,
        new CorrelationId($"corr-{sequence}"),
        Morning);
}
