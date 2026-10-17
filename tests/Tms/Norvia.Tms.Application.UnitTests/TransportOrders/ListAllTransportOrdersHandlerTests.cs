using Ardalis.SharedKernel;
using Norvia.Tms.Application.Shippers;
using Norvia.Tms.Application.TransportOrders;
using Norvia.Tms.Application.TransportOrders.Browse;
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
    private readonly ListAllTransportOrdersHandler _handler;

    public ListAllTransportOrdersHandlerTests()
    {
        _shippers.Find(new ShipperId("SHP-0001")).Returns(new Shipper(new ShipperId("SHP-0001"), "Brouwer Dranken", true));
        _shipments.ListAsync(Arg.Any<ShipmentsOfTransportOrdersSpec>(), Arg.Any<CancellationToken>()).Returns([]);
        _handler = new ListAllTransportOrdersHandler(_transportOrders, _shipments, _shippers);
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

    private void Lists(int totalCount, params TransportOrder[] orders)
    {
        _transportOrders.ListAsync(Arg.Any<TransportOrdersSearchSpec>(), Arg.Any<CancellationToken>()).Returns(orders.ToList());
        _transportOrders.CountAsync(Arg.Any<TransportOrdersSearchSpec>(), Arg.Any<CancellationToken>()).Returns(totalCount);
    }
}
