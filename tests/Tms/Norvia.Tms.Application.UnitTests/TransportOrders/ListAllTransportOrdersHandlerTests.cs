using Ardalis.SharedKernel;
using Norvia.Tms.Application.Carriers;
using Norvia.Tms.Application.Shipments;
using Norvia.Tms.Application.Shippers;
using Norvia.Tms.Application.TransportOrders;
using Norvia.Tms.Application.TransportOrders.Browse;
using Norvia.Tms.Domain.Carriers;
using Norvia.Tms.Domain.Shipments;
using Norvia.Tms.Domain.Shippers;
using Norvia.Tms.Domain.TransportOrders;
using NSubstitute;

namespace Norvia.Tms.Application.UnitTests.TransportOrders;

public sealed class ListAllTransportOrdersHandlerTests
{
    private readonly IReadRepository<TransportOrder> _transportOrders = Substitute.For<IReadRepository<TransportOrder>>();
    private readonly IReadRepository<Shipment> _shipments = Substitute.For<IReadRepository<Shipment>>();
    private readonly IShipperDirectory _shippers = Substitute.For<IShipperDirectory>();
    private readonly ICarrierDirectory _carriers = Substitute.For<ICarrierDirectory>();
    private readonly ListAllTransportOrdersHandler _handler;

    public ListAllTransportOrdersHandlerTests()
    {
        _shippers.Find(new ShipperId("SHP-0001")).Returns(new Shipper(new ShipperId("SHP-0001"), "Brouwer Dranken", true));
        _carriers.Find(new CarrierId("CAR-0001")).Returns(new Carrier(new CarrierId("CAR-0001"), "Van Dijk Transport", true));
        _shipments.ListAsync(Arg.Any<ShipmentsOfTransportOrdersSpec>(), Arg.Any<CancellationToken>()).Returns([]);
        _handler = new ListAllTransportOrdersHandler(_transportOrders, _shipments, _shippers, _carriers);
    }

    [Fact]
    public async Task Handle_AnAcceptedOrder_ListsItWithShipperPlacesAndCollectionWindowAndNoShipment()
    {
        Lists(totalCount: 1, Samples.AcceptedOrder(sequence: 7));

        var page = await _handler.Handle(new ListAllTransportOrdersQuery(), CancellationToken.None);

        page.Items.Should().Equal(new TransportOrderListItem(
            "TO-0000007",
            TransportOrderStatus.Accepted,
            new ShipperView("SHP-0001", "Brouwer Dranken"),
            "BD-2026-0001",
            new PlaceView("3087 BM", "Rotterdam", "NL"),
            new PlaceView("3511 AW", "Utrecht", "NL"),
            Samples.Now.AddDays(1),
            Samples.Now.AddDays(1).AddHours(4),
            null,
            null,
            null));
    }

    [Fact]
    public async Task Handle_APlannedOrder_ListsItsShipmentNumberAndStatus()
    {
        var order = Samples.AcceptedOrder(sequence: 7);
        var shipment = order.Plan(ShipmentNumber.FromSequence(12), CarriedBy.OwnFleet, new IdempotencyKey("key"), new CorrelationId("corr-plan"), Samples.Now)!;
        Lists(totalCount: 1, order);
        _shipments.ListAsync(Arg.Any<ShipmentsOfTransportOrdersSpec>(), Arg.Any<CancellationToken>()).Returns([shipment]);

        var page = await _handler.Handle(new ListAllTransportOrdersQuery(), CancellationToken.None);

        var item = page.Items.Should().ContainSingle().Subject;
        item.Status.Should().Be(TransportOrderStatus.Planned);
        item.ShipmentNumber.Should().Be("SH-0000012");
        item.ShipmentStatus.Should().Be(ShipmentStatus.Planned);
    }

    [Fact]
    public async Task Handle_ACarrierShipment_ListsTheCarrierByName()
    {
        var item = await ListPlanned(CarriedBy.Carrier(new CarrierId("CAR-0001")));

        item.CarriedBy.Should().Be(new CarriedByView(AssignedTo.Carrier, new CarrierView("CAR-0001", "Van Dijk Transport")));
    }

    [Fact]
    public async Task Handle_AnOwnFleetShipment_ListsOwnFleet()
    {
        var item = await ListPlanned(CarriedBy.OwnFleet);

        item.CarriedBy.Should().Be(new CarriedByView(AssignedTo.OwnFleet, null));
    }

    [Fact]
    public async Task Handle_AReassignedShipment_ListsItsNewAssignee()
    {
        var order = Samples.AcceptedOrder(sequence: 7);
        var shipment = order.Plan(ShipmentNumber.FromSequence(12), CarriedBy.Carrier(new CarrierId("CAR-0001")), new IdempotencyKey("key"), new CorrelationId("corr-plan"), Samples.Now)!;
        shipment.Reassign(CarriedBy.OwnFleet, new IdempotencyKey("key-2"), new CorrelationId("corr-reassign"), Samples.Now.AddMinutes(5));
        Lists(totalCount: 1, order);
        _shipments.ListAsync(Arg.Any<ShipmentsOfTransportOrdersSpec>(), Arg.Any<CancellationToken>()).Returns([shipment]);

        var page = await _handler.Handle(new ListAllTransportOrdersQuery(), CancellationToken.None);

        page.Items.Should().ContainSingle().Which.CarriedBy.Should().Be(new CarriedByView(AssignedTo.OwnFleet, null));
    }

    [Theory]
    [InlineData("CAR-0001")]
    [InlineData(ListAllTransportOrdersQuery.OwnFleet)]
    public async Task Handle_FilteredOnWhoCarries_HandsTheOrdersOfThoseShipmentsToTheSearch(string carriedBy)
    {
        var order = Samples.AcceptedOrder(sequence: 7);
        var shipment = order.Plan(ShipmentNumber.FromSequence(12), CarriedBy.OwnFleet, new IdempotencyKey("key"), new CorrelationId("corr-plan"), Samples.Now)!;
        Lists(totalCount: 1, order);
        _shipments.ListAsync(Arg.Any<ShipmentsCarriedBySpec>(), Arg.Any<CancellationToken>()).Returns([shipment]);
        _shipments.ListAsync(Arg.Any<ShipmentsOfTransportOrdersSpec>(), Arg.Any<CancellationToken>()).Returns([shipment]);

        var page = await _handler.Handle(new ListAllTransportOrdersQuery(CarriedBy: carriedBy), CancellationToken.None);

        page.Items.Should().ContainSingle();
        await _shipments.Received(1).ListAsync(Arg.Any<ShipmentsCarriedBySpec>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_NothingIsCarriedByWhoWasChosen_ListsNothing()
    {
        _shipments.ListAsync(Arg.Any<ShipmentsCarriedBySpec>(), Arg.Any<CancellationToken>()).Returns([]);
        Lists(totalCount: 0);

        var page = await _handler.Handle(new ListAllTransportOrdersQuery(CarriedBy: "CAR-0001"), CancellationToken.None);

        page.Items.Should().BeEmpty();
        await _transportOrders.Received(1).ListAsync(Arg.Is<TransportOrdersSearchSpec>(spec => spec.Take == 25), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_FilteredOnShipmentStatus_HandsTheOrdersOfThoseShipmentsToTheSearch()
    {
        var order = Samples.AcceptedOrder(sequence: 7);
        var shipment = order.Plan(ShipmentNumber.FromSequence(12), CarriedBy.OwnFleet, new IdempotencyKey("key"), new CorrelationId("corr-plan"), Samples.Now)!;
        Lists(totalCount: 1, order);
        _shipments.ListAsync(Arg.Any<ShipmentsOfStatusSpec>(), Arg.Any<CancellationToken>()).Returns([shipment]);
        _shipments.ListAsync(Arg.Any<ShipmentsOfTransportOrdersSpec>(), Arg.Any<CancellationToken>()).Returns([shipment]);

        var page = await _handler.Handle(new ListAllTransportOrdersQuery(ShipmentStatus: ShipmentStatus.Planned), CancellationToken.None);

        page.Items.Should().ContainSingle();
        await _shipments.Received(1).ListAsync(Arg.Any<ShipmentsOfStatusSpec>(), Arg.Any<CancellationToken>());
        await _transportOrders.Received(1).ListAsync(Arg.Is<TransportOrdersSearchSpec>(spec => spec.Take == 25), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_FilteredOnShipmentStatusAndWhoCarries_KeepsOnlyOrdersMatchingBoth()
    {
        var both = Samples.AcceptedOrder(sequence: 7);
        var other = Samples.AcceptedOrder(sequence: 8);
        var bothShipment = both.Plan(ShipmentNumber.FromSequence(12), CarriedBy.OwnFleet, new IdempotencyKey("key"), new CorrelationId("corr-plan"), Samples.Now)!;
        var otherShipment = other.Plan(ShipmentNumber.FromSequence(13), CarriedBy.OwnFleet, new IdempotencyKey("key-2"), new CorrelationId("corr-plan-2"), Samples.Now)!;
        Lists(totalCount: 1, both);
        _shipments.ListAsync(Arg.Any<ShipmentsCarriedBySpec>(), Arg.Any<CancellationToken>()).Returns([bothShipment, otherShipment]);
        _shipments.ListAsync(Arg.Any<ShipmentsOfStatusSpec>(), Arg.Any<CancellationToken>()).Returns([bothShipment]);
        _shipments.ListAsync(Arg.Any<ShipmentsOfTransportOrdersSpec>(), Arg.Any<CancellationToken>()).Returns([bothShipment]);

        await _handler.Handle(new ListAllTransportOrdersQuery(CarriedBy: ListAllTransportOrdersQuery.OwnFleet, ShipmentStatus: ShipmentStatus.Planned), CancellationToken.None);

        await _transportOrders.Received(1).ListAsync(
            Arg.Is<TransportOrdersSearchSpec>(spec => Matches(spec, both) && !Matches(spec, other)), Arg.Any<CancellationToken>());
    }

    private static bool Matches(TransportOrdersSearchSpec spec, TransportOrder order) => spec.Evaluate([order]).Any();

    [Fact]
    public async Task Handle_WithoutAWhoCarriesFilter_DoesNotReadShipmentsToFilter()
    {
        Lists(totalCount: 0);

        await _handler.Handle(new ListAllTransportOrdersQuery(CarriedBy: " "), CancellationToken.None);

        await _shipments.DidNotReceive().ListAsync(Arg.Any<ShipmentsCarriedBySpec>(), Arg.Any<CancellationToken>());
        await _shipments.DidNotReceive().ListAsync(Arg.Any<ShipmentsOfStatusSpec>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_APage_ReportsTheTotalAcrossAllPagesAndItsOwnPaging()
    {
        Lists(totalCount: 51, Samples.AcceptedOrder(sequence: 7));

        var page = await _handler.Handle(new ListAllTransportOrdersQuery(Page: 2, PageSize: 25), CancellationToken.None);

        (page.Page, page.PageSize, page.TotalCount, page.PageCount).Should().Be((2, 25, 51, 3));
    }

    [Fact]
    public async Task Handle_FiltersAndSearch_AreHandedToTheDatabaseInTheSpecification()
    {
        Lists(totalCount: 0);

        await _handler.Handle(new ListAllTransportOrdersQuery(TransportOrderStatus.Planned, "SHP-0001", "bd-2026", 3, 10), CancellationToken.None);

        await _transportOrders.Received(1).ListAsync(
            Arg.Is<TransportOrdersSearchSpec>(spec => spec.Skip == 20 && spec.Take == 10), Arg.Any<CancellationToken>());
        await _transportOrders.Received(1).CountAsync(Arg.Any<TransportOrdersSearchSpec>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(0, 0, 1, 1)]
    [InlineData(-4, 1_000, 1, ListAllTransportOrdersQuery.MaxPageSize)]
    public async Task Handle_APageOrPageSizeOutOfRange_IsBroughtIntoRange(int requestedPage, int requestedSize, int expectedPage, int expectedSize)
    {
        Lists(totalCount: 0);

        var page = await _handler.Handle(new ListAllTransportOrdersQuery(Page: requestedPage, PageSize: requestedSize), CancellationToken.None);

        (page.Page, page.PageSize).Should().Be((expectedPage, expectedSize));
    }

    [Fact]
    public async Task Handle_NothingMatches_ReturnsAnEmptyPage()
    {
        Lists(totalCount: 0);

        var page = await _handler.Handle(new ListAllTransportOrdersQuery(Search: "no-such-text"), CancellationToken.None);

        page.Items.Should().BeEmpty();
        page.TotalCount.Should().Be(0);
        page.PageCount.Should().Be(0);
    }

    [Fact]
    public async Task Handle_ARejectedBookingInTheResult_FailsInsteadOfDroppingOutUnseen()
    {
        Lists(totalCount: 1, Samples.RejectedOrder());

        var list = async () => await _handler.Handle(new ListAllTransportOrdersQuery(), CancellationToken.None);

        await list.Should().ThrowAsync<InvalidOperationException>();
    }

    private async Task<TransportOrderListItem> ListPlanned(CarriedBy carriedBy)
    {
        var order = Samples.AcceptedOrder(sequence: 7);
        var shipment = order.Plan(ShipmentNumber.FromSequence(12), carriedBy, new IdempotencyKey("key"), new CorrelationId("corr-plan"), Samples.Now)!;
        Lists(totalCount: 1, order);
        _shipments.ListAsync(Arg.Any<ShipmentsOfTransportOrdersSpec>(), Arg.Any<CancellationToken>()).Returns([shipment]);

        var page = await _handler.Handle(new ListAllTransportOrdersQuery(), CancellationToken.None);

        return page.Items.Should().ContainSingle().Subject;
    }

    private void Lists(int totalCount, params TransportOrder[] orders)
    {
        _transportOrders.ListAsync(Arg.Any<TransportOrdersSearchSpec>(), Arg.Any<CancellationToken>()).Returns(orders.ToList());
        _transportOrders.CountAsync(Arg.Any<TransportOrdersSearchSpec>(), Arg.Any<CancellationToken>()).Returns(totalCount);
    }
}
