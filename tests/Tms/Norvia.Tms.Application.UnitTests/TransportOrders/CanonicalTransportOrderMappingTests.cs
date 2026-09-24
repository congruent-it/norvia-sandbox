using Norvia.Tms.Application.CanonicalModel;

namespace Norvia.Tms.Application.UnitTests.TransportOrders;

public sealed class CanonicalTransportOrderMappingTests
{
    [Fact]
    public void ToDetails_ThenBack_KeepsEverythingTheShipperSubmitted()
    {
        var submitted = Samples.CanonicalTransportOrder();

        var roundTripped = CanonicalTransportOrder.From(Samples.AcceptedOrder());

        roundTripped.Should().BeEquivalentTo(submitted with { TransportOrderNumber = "TO-0000007" });
    }

    [Fact]
    public void ToDetails_OnlyShipperIdGiven_KeepsTheGapsAsSubmitted()
    {
        var sparse = new CanonicalTransportOrder(null, "SHP-0001", null, null, null, null, null);

        var details = sparse.ToDetails();

        details.ShipperReference.Should().BeNull();
        details.Collection.Should().BeNull();
        details.Delivery.Should().BeNull();
        details.GoodsLines.Should().BeEmpty();
        details.Instructions.Should().BeNull();
    }

    [Fact]
    public void ToDetails_PartsWithGaps_KeepsTheGapsAsSubmitted()
    {
        var sparse = new CanonicalTransportOrder(
            null,
            "SHP-0001",
            "BD-1",
            new CanonicalCollectionDetails(null, null),
            new CanonicalDeliveryDetails(null, null, null),
            [new CanonicalGoodsLine(null, null, null, null)],
            null);

        var details = sparse.ToDetails();

        details.Collection!.Address.Should().BeNull();
        details.Collection.Window.Should().BeNull();
        details.Delivery!.Consignee.Should().BeNull();
        details.GoodsLines.Should().ContainSingle().Which.DimensionsCm.Should().BeNull();
    }
}
