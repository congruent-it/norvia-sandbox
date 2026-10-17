using Norvia.Tms.Domain.Shipments;
using Norvia.Tms.Domain.Shippers;
using Norvia.Tms.Domain.TransportOrders;

namespace Norvia.Tms.Domain.UnitTests.TransportOrders;

public sealed class TransportOrdersSearchSpecTests
{
    private static readonly DateTimeOffset Morning = new(2026, 9, 24, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Evaluated_ListsAcceptedAndPlannedOrdersNewestFirst_AndNoRejectedBooking()
    {
        var oldest = Accepted(1, "SHP-0001", "BD-1", Morning);
        var newest = Planned(2, "SHP-0001", "BD-2", Morning.AddHours(2), shipmentSequence: 9);
        var middle = Accepted(3, "SHP-0002", "BD-3", Morning.AddHours(1));
        var rejected = TransportOrder.Reject(
            new ShipperId("SHP-0001"), TransportOrderTests.SampleDetails(), [new RejectionReason(RejectionCode.GoodsLinesMissing, "None.")],
            IntakeChannel.ShipperApi, new CorrelationId("corr-rejected"), Morning.AddHours(3));

        var listed = Spec().Evaluate([oldest, rejected, newest, middle]);

        listed.Select(order => order.Number!.Value).Should().Equal("TO-0000002", "TO-0000003", "TO-0000001");
    }

    [Fact]
    public void Evaluated_OrdersReceivedAtTheSameTime_NewestNumberFirst()
    {
        var lower = Accepted(1, "SHP-0001", "BD-1", Morning);
        var higher = Accepted(2, "SHP-0001", "BD-2", Morning);

        Spec().Evaluate([lower, higher]).Select(order => order.Number!.Value).Should().Equal("TO-0000002", "TO-0000001");
    }

    [Theory]
    [InlineData(TransportOrderStatus.Accepted, "TO-0000001")]
    [InlineData(TransportOrderStatus.Planned, "TO-0000002")]
    public void Evaluated_WithAStatus_ListsOnlyThatStatus(TransportOrderStatus status, string expected)
    {
        var accepted = Accepted(1, "SHP-0001", "BD-1", Morning);
        var planned = Planned(2, "SHP-0001", "BD-2", Morning, shipmentSequence: 9);

        Spec(status: status).Evaluate([accepted, planned]).Should().ContainSingle().Which.Number!.Value.Should().Be(expected);
    }

    [Fact]
    public void Evaluated_WithAShipper_ListsOnlyThatShippersOrders()
    {
        var own = Accepted(1, "SHP-0001", "BD-1", Morning);
        var anothers = Accepted(2, "SHP-0002", "BD-2", Morning);

        Spec(shipper: "SHP-0002").Evaluate([own, anothers]).Should().ContainSingle().Which.Should().BeSameAs(anothers);
    }

    [Theory]
    [InlineData("0000042", "TO-0000042")]
    [InlineData("to-0000042", "TO-0000042")]
    [InlineData("  TO-0000042  ", "TO-0000042")]
    [InlineData("bd-2026-77", "TO-0000043")]
    [InlineData("BD-2026-77", "TO-0000043")]
    [InlineData("sh-0000009", "TO-0000044")]
    [InlineData("0000009", "TO-0000044")]
    public void Evaluated_WithSearchText_MatchesTheNumberTheShipperReferenceAndTheShipmentNumberContainingItWhateverItsCase(string search, string expected)
    {
        var byNumber = Accepted(42, "SHP-0001", "ref-a", Morning);
        var byReference = Accepted(43, "SHP-0001", "bd-2026-77", Morning);
        var byShipment = Planned(44, "SHP-0001", "ref-c", Morning, shipmentSequence: 9);

        Spec(search: search).Evaluate([byNumber, byReference, byShipment]).Select(order => order.Number!.Value).Should().Equal(expected);
    }

    [Fact]
    public void Evaluated_WithSearchTextThatMatchesNothing_ListsNothing()
    {
        Spec(search: "no-such-text").Evaluate([Accepted(1, "SHP-0001", "BD-1", Morning)]).Should().BeEmpty();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Evaluated_WithoutSearchText_FiltersNothing(string? search)
    {
        Spec(search: search).Evaluate([Accepted(1, "SHP-0001", "BD-1", Morning), Accepted(2, "SHP-0001", "BD-2", Morning)]).Should().HaveCount(2);
    }

    [Fact]
    public void Evaluated_SearchCombinesWithTheFilters()
    {
        var match = Planned(1, "SHP-0001", "BD-A", Morning, shipmentSequence: 9);
        var otherShipper = Planned(2, "SHP-0002", "BD-A", Morning, shipmentSequence: 10);
        var otherStatus = Accepted(3, "SHP-0001", "BD-A", Morning);
        var otherText = Planned(4, "SHP-0001", "BD-B", Morning, shipmentSequence: 11);

        Spec(TransportOrderStatus.Planned, "SHP-0001", "bd-a").Evaluate([match, otherShipper, otherStatus, otherText])
            .Should().ContainSingle().Which.Should().BeSameAs(match);
    }

    [Fact]
    public void Evaluated_ReturnsOnlyTheRequestedPage()
    {
        var orders = Enumerable.Range(1, 5).Select(sequence => Accepted(sequence, "SHP-0001", $"BD-{sequence}", Morning.AddHours(sequence))).ToList();

        Spec(page: 2, pageSize: 2).Evaluate(orders).Select(order => order.Number!.Value).Should().Equal("TO-0000003", "TO-0000002");
        Spec(page: 3, pageSize: 2).Evaluate(orders).Select(order => order.Number!.Value).Should().Equal("TO-0000001");
        Spec(page: 4, pageSize: 2).Evaluate(orders).Should().BeEmpty();
    }

    [Theory]
    [InlineData(0, 25)]
    [InlineData(1, 0)]
    public void New_WithAPageOrPageSizeBelowOne_Throws(int page, int pageSize)
    {
        var create = () => Spec(page: page, pageSize: pageSize);

        create.Should().Throw<ArgumentOutOfRangeException>();
    }

    private static TransportOrdersSearchSpec Spec(
        TransportOrderStatus? status = null, string? shipper = null, string? search = null, int page = 1, int pageSize = 25) =>
        new(status, shipper is null ? null : new ShipperId(shipper), search, page, pageSize);

    private static TransportOrder Accepted(long sequence, string shipperId, string reference, DateTimeOffset receivedAt) => TransportOrder.Accept(
        new ShipperId(shipperId),
        TransportOrderTests.SampleDetails() with { ShipperReference = reference },
        TransportOrderNumber.FromSequence(sequence),
        IntakeChannel.ShipperApi,
        new CorrelationId($"corr-{sequence}"),
        receivedAt);

    private static TransportOrder Planned(long sequence, string shipperId, string reference, DateTimeOffset receivedAt, long shipmentSequence)
    {
        var order = Accepted(sequence, shipperId, reference, receivedAt);
        order.Plan(ShipmentNumber.FromSequence(shipmentSequence), CarriedBy.OwnFleet, new IdempotencyKey($"key-{sequence}"), new CorrelationId($"corr-plan-{sequence}"), receivedAt);
        return order;
    }
}
