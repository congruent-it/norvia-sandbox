using Norvia.Tms.Domain.Shipments;
using Norvia.Tms.Domain.TransportOrders;
using Norvia.Tms.Domain.UnitTests.TransportOrders;

namespace Norvia.Tms.Domain.UnitTests.Shipments;

public sealed class ShipmentsOfStatusSpecTests
{
    [Fact]
    public void Evaluated_ListsOnlyTheShipmentsInThatStatus()
    {
        var (_, planned) = Planned(1);
        var (order, released) = Planned(2);
        released.Release(order, new IdempotencyKey("key-release"), new CorrelationId("corr-release"), new DateTimeOffset(2026, 9, 24, 10, 0, 0, TimeSpan.Zero));

        new ShipmentsOfStatusSpec(ShipmentStatus.Released).Evaluate([planned, released]).Should().ContainSingle().Which.Should().BeSameAs(released);
        new ShipmentsOfStatusSpec(ShipmentStatus.Delivered).Evaluate([planned, released]).Should().BeEmpty();
    }

    private static (TransportOrder Order, Shipment Shipment) Planned(long sequence)
    {
        var order = TransportOrder.Accept(
            new Norvia.Tms.Domain.Shippers.ShipperId("SHP-0001"), TransportOrderTests.SampleDetails(), TransportOrderNumber.FromSequence(sequence),
            IntakeChannel.ShipperApi, new CorrelationId($"corr-{sequence}"), new DateTimeOffset(2026, 9, 24, 8, 0, 0, TimeSpan.Zero));
        return (order, order.Plan(ShipmentNumber.FromSequence(sequence), CarriedBy.OwnFleet, new IdempotencyKey($"key-{sequence}"), new CorrelationId($"corr-plan-{sequence}"), new DateTimeOffset(2026, 9, 24, 9, 0, 0, TimeSpan.Zero))!);
    }
}
