using Ardalis.SharedKernel;
using Norvia.Tms.Application.CanonicalModel;
using Norvia.Tms.Application.Shippers;
using Norvia.Tms.Application.TransportOrders;
using Norvia.Tms.Application.TransportOrders.ToPlan;
using Norvia.Tms.Domain.Shippers;
using Norvia.Tms.Domain.TransportOrders;
using NSubstitute;

namespace Norvia.Tms.Application.UnitTests.TransportOrders;

public sealed class TransportOrdersToPlanQueryTests
{
    private readonly IReadRepository<TransportOrder> _repository = Substitute.For<IReadRepository<TransportOrder>>();
    private readonly IShipperDirectory _shippers = Substitute.For<IShipperDirectory>();
    private readonly ListTransportOrdersToPlanHandler _handler;

    public TransportOrdersToPlanQueryTests()
    {
        _shippers.Find(new ShipperId("SHP-0001")).Returns(new Shipper(new ShipperId("SHP-0001"), "Brouwer Dranken", true));
        _handler = new ListTransportOrdersToPlanHandler(_repository, _shippers);
    }

    [Fact]
    public async Task Handle_AcceptedOrder_ListsItWithShipperPlacesCollectionWindowAndRatingResult()
    {
        var order = Samples.RatingFailedOrder(sequence: 7);
        ListsAccepted(order);

        var toPlan = await _handler.Handle(new ListTransportOrdersToPlanQuery(), CancellationToken.None);

        toPlan.Should().Equal(new TransportOrderToPlanSummary(
            "TO-0000007",
            RatingResult.RatingFailed,
            new ShipperView("SHP-0001", "Brouwer Dranken"),
            "BD-2026-0001",
            new PlaceView("3087 BM", "Rotterdam", "NL"),
            new PlaceView("3511 AW", "Utrecht", "NL"),
            Samples.Now.AddDays(1),
            Samples.Now.AddDays(1).AddHours(4),
            Samples.Now));
    }

    [Fact]
    public async Task Handle_SeveralOrders_SortsThemByEarliestCollectionTime()
    {
        var dayAfter = CollectedFrom(sequence: 7, Samples.Now.AddDays(2));
        var tomorrow = CollectedFrom(sequence: 8, Samples.Now.AddDays(1));
        var tomorrowEarlier = CollectedFrom(sequence: 9, Samples.Now.AddDays(1).AddHours(-3));
        ListsAccepted(dayAfter, tomorrow, tomorrowEarlier);

        var toPlan = await _handler.Handle(new ListTransportOrdersToPlanQuery(), CancellationToken.None);

        toPlan.Select(order => order.TransportOrderNumber).Should().Equal("TO-0000009", "TO-0000008", "TO-0000007");
    }

    [Fact]
    public async Task Handle_OrdersCollectedAtTheSameTime_ListsTheOneReceivedFirstFirst()
    {
        var collection = Samples.Now.AddDays(1);
        var later = CollectedFrom(sequence: 7, collection, receivedAt: Samples.Now.AddMinutes(5));
        var earlier = CollectedFrom(sequence: 8, collection, receivedAt: Samples.Now);
        ListsAccepted(later, earlier);

        var toPlan = await _handler.Handle(new ListTransportOrdersToPlanQuery(), CancellationToken.None);

        toPlan.Select(order => order.TransportOrderNumber).Should().Equal("TO-0000008", "TO-0000007");
    }

    [Fact]
    public async Task Handle_NoAcceptedOrders_ListsNothing()
    {
        ListsAccepted();

        var toPlan = await _handler.Handle(new ListTransportOrdersToPlanQuery(), CancellationToken.None);

        toPlan.Should().BeEmpty();
    }

    // Only Accepted orders are to plan: a Planned one has its Shipment, and a Rejected booking is never planned.
    private void ListsAccepted(params TransportOrder[] orders) =>
        _repository.ListAsync(Arg.Is<TransportOrdersByStatusSpec>(spec => spec.Status == TransportOrderStatus.Accepted), Arg.Any<CancellationToken>())
            .Returns(orders.ToList());

    private static TransportOrder CollectedFrom(long sequence, DateTimeOffset earliest, DateTimeOffset? receivedAt = null)
    {
        var submitted = Samples.CanonicalTransportOrder();
        submitted = submitted with
        {
            Collection = submitted.Collection! with { Window = new CanonicalTimeWindow(earliest, earliest.AddHours(4)) },
        };
        return TransportOrder.Accept(
            new ShipperId("SHP-0001"),
            submitted.ToDetails(),
            TransportOrderNumber.FromSequence(sequence),
            IntakeChannel.ShipperApi,
            new CorrelationId($"corr-{sequence}"),
            receivedAt ?? Samples.Now);
    }
}
