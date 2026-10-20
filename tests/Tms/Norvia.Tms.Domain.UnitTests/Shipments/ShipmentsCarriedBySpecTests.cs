using Norvia.Tms.Domain.Carriers;
using Norvia.Tms.Domain.Shipments;
using Norvia.Tms.Domain.TransportOrders;
using Norvia.Tms.Domain.UnitTests.TransportOrders;

namespace Norvia.Tms.Domain.UnitTests.Shipments;

public sealed class ShipmentsCarriedBySpecTests
{
    private static readonly CarrierId VanDijk = new("CAR-0001");
    private static readonly CarrierId Kessler = new("CAR-0002");

    [Fact]
    public void Evaluated_ForACarrier_ListsOnlyItsShipments()
    {
        var van = Planned(1, CarriedBy.Carrier(VanDijk));
        var kessler = Planned(2, CarriedBy.Carrier(Kessler));
        var own = Planned(3, CarriedBy.OwnFleet);

        new ShipmentsCarriedBySpec(CarriedBy.Carrier(VanDijk)).Evaluate([van, kessler, own]).Should().ContainSingle().Which.Should().BeSameAs(van);
    }

    [Fact]
    public void Evaluated_ForTheOwnFleet_ListsOnlyItsShipments()
    {
        var van = Planned(1, CarriedBy.Carrier(VanDijk));
        var own = Planned(3, CarriedBy.OwnFleet);

        new ShipmentsCarriedBySpec(CarriedBy.OwnFleet).Evaluate([van, own]).Should().ContainSingle().Which.Should().BeSameAs(own);
    }

    private static Shipment Planned(long sequence, CarriedBy carriedBy)
    {
        var order = TransportOrder.Accept(
            new Norvia.Tms.Domain.Shippers.ShipperId("SHP-0001"), TransportOrderTests.SampleDetails(), TransportOrderNumber.FromSequence(sequence),
            IntakeChannel.ShipperApi, new CorrelationId($"corr-{sequence}"), new DateTimeOffset(2026, 9, 24, 8, 0, 0, TimeSpan.Zero));
        return order.Plan(ShipmentNumber.FromSequence(sequence), carriedBy, new IdempotencyKey($"key-{sequence}"), new CorrelationId($"corr-plan-{sequence}"), new DateTimeOffset(2026, 9, 24, 9, 0, 0, TimeSpan.Zero))!;
    }
}
