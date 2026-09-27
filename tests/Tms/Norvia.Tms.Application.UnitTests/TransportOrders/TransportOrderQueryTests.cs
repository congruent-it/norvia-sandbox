using Ardalis.Result;
using Ardalis.SharedKernel;
using Norvia.Tms.Application.CanonicalModel;
using Norvia.Tms.Application.Shippers;
using Norvia.Tms.Application.TransportOrders;
using Norvia.Tms.Application.TransportOrders.GetById;
using Norvia.Tms.Application.TransportOrders.GetByNumber;
using Norvia.Tms.Application.TransportOrders.GetRejectedBooking;
using Norvia.Tms.Application.TransportOrders.List;
using Norvia.Tms.Application.TransportOrders.RatingExceptions;
using Norvia.Tms.Domain.Shippers;
using Norvia.Tms.Domain.TransportOrders;
using NSubstitute;

namespace Norvia.Tms.Application.UnitTests.TransportOrders;

public sealed class TransportOrderQueryTests
{
    private readonly IReadRepository<TransportOrder> _repository = Substitute.For<IReadRepository<TransportOrder>>();
    private readonly IShipperDirectory _shippers = Substitute.For<IShipperDirectory>();

    public TransportOrderQueryTests()
    {
        _shippers.Find(new ShipperId("SHP-0001")).Returns(new Shipper(new ShipperId("SHP-0001"), "Brouwer Dranken", true));
    }

    [Fact]
    public async Task Get_KnownNumber_ReturnsTheTransportOrderWithItsShipper()
    {
        var order = Samples.AcceptedOrder(sequence: 5);
        _repository.FirstOrDefaultAsync(Arg.Is<TransportOrderByNumberSpec>(s => s.Number.Value == "TO-0000005"), Arg.Any<CancellationToken>())
            .Returns(order);

        var result = await new GetTransportOrderHandler(_repository, _shippers).Handle(new GetTransportOrderQuery("TO-0000005"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeEquivalentTo(new TransportOrderView(
            order.Id.Value,
            "TO-0000005",
            TransportOrderStatus.Accepted,
            new ShipperView("SHP-0001", "Brouwer Dranken"),
            IntakeChannel.ShipperApi,
            "corr-1",
            Samples.Now,
            [],
            RatingResult.NotYetRated,
            null,
            null,
            CanonicalTransportOrder.From(order)));
    }

    [Fact]
    public async Task Get_RatedOrder_ShowsItsFreightChargeWithItsChargeLinesAndRateCardVersion()
    {
        var order = Samples.RatedOrder(sequence: 5);
        _repository.FirstOrDefaultAsync(Arg.Any<TransportOrderByNumberSpec>(), Arg.Any<CancellationToken>()).Returns(order);

        var result = await new GetTransportOrderHandler(_repository, _shippers).Handle(new GetTransportOrderQuery("TO-0000005"), CancellationToken.None);

        result.Value.RatingResult.Should().Be(RatingResult.Rated);
        result.Value.FreightCharge.Should().Be(new FreightChargeView(
            "EUR",
            112.90m,
            [new ChargeLineView("Freight", 112.90m)],
            new RateCardVersionView("RC-0001", 3)));
    }

    [Fact]
    public async Task Get_RatingFailedOrder_ShowsTheFailureCodeAndMessage_AndStaysAccepted()
    {
        var order = Samples.RatingFailedOrder(sequence: 5);
        _repository.FirstOrDefaultAsync(Arg.Any<TransportOrderByNumberSpec>(), Arg.Any<CancellationToken>()).Returns(order);

        var result = await new GetTransportOrderHandler(_repository, _shippers).Handle(new GetTransportOrderQuery("TO-0000005"), CancellationToken.None);

        result.Value.Status.Should().Be(TransportOrderStatus.Accepted);
        result.Value.RatingResult.Should().Be(RatingResult.RatingFailed);
        result.Value.RatingFailure.Should().Be(new RatingFailureView("NoRateCard", "Shipper SHP-0001 has no Rate Card."));
        result.Value.FreightCharge.Should().BeNull();
    }

    [Fact]
    public async Task Get_UnknownNumber_ReturnsNotFound()
    {
        var result = await new GetTransportOrderHandler(_repository, _shippers).Handle(new GetTransportOrderQuery("TO-0000099"), CancellationToken.None);

        result.Status.Should().Be(ResultStatus.NotFound);
    }

    [Fact]
    public async Task Get_MalformedNumber_ReturnsNotFound()
    {
        var result = await new GetTransportOrderHandler(_repository, _shippers).Handle(new GetTransportOrderQuery("42"), CancellationToken.None);

        result.Status.Should().Be(ResultStatus.NotFound);
    }

    [Fact]
    public async Task GetById_RejectedBooking_ReturnsItsReasonsAndSubmittedContentWithoutANumber()
    {
        var order = Samples.RejectedOrder();
        _repository.FirstOrDefaultAsync(Arg.Is<TransportOrderByIdSpec>(s => s.Id == order.Id), Arg.Any<CancellationToken>())
            .Returns(order);

        var result = await new GetTransportOrderByIdHandler(_repository, _shippers).Handle(new GetTransportOrderByIdQuery(order.Id.Value), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeEquivalentTo(new TransportOrderView(
            order.Id.Value,
            null,
            TransportOrderStatus.Rejected,
            new ShipperView("SHP-0001", "Brouwer Dranken"),
            IntakeChannel.ShipperApi,
            "corr-2",
            Samples.Now,
            [
                new RejectionReasonView("InactiveShipper", "The Shipper is not active, so Norvia can't take bookings for it."),
                new RejectionReasonView("GoodsLinesMissing", "The booking has no goods lines."),
            ],
            null,
            null,
            null,
            Samples.CanonicalTransportOrder()));
    }

    [Fact]
    public async Task GetRejectedBooking_ARejectedBookingsId_ReturnsItWithItsShipperAndReasons()
    {
        var order = Samples.RejectedOrder();
        _repository.FirstOrDefaultAsync(Arg.Is<RejectedBookingByIdSpec>(s => s.Id == order.Id), Arg.Any<CancellationToken>())
            .Returns(order);

        var result = await new GetRejectedBookingHandler(_repository, _shippers).Handle(new GetRejectedBookingQuery(order.Id.Value), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Status.Should().Be(TransportOrderStatus.Rejected);
        result.Value.Shipper.Should().Be(new ShipperView("SHP-0001", "Brouwer Dranken"));
        result.Value.RejectionReasons.Should().Equal(RejectionReasonView.From(Samples.Reasons));
    }

    [Fact]
    public async Task GetRejectedBooking_AnIdItDoesntFind_ReturnsNotFound()
    {
        var result = await new GetRejectedBookingHandler(_repository, _shippers).Handle(new GetRejectedBookingQuery(Guid.NewGuid()), CancellationToken.None);

        result.Status.Should().Be(ResultStatus.NotFound);
    }

    [Fact]
    public async Task GetById_UnknownId_ReturnsNotFound()
    {
        var result = await new GetTransportOrderByIdHandler(_repository, _shippers).Handle(new GetTransportOrderByIdQuery(Guid.NewGuid()), CancellationToken.None);

        result.Status.Should().Be(ResultStatus.NotFound);
    }

    [Fact]
    public async Task ListRatingExceptions_RatingFailedOrders_ReturnsThemWithTheirFailure()
    {
        var failed = Samples.RatingFailedOrder(sequence: 4);
        _repository.ListAsync(Arg.Any<TransportOrdersRatingFailedSpec>(), Arg.Any<CancellationToken>()).Returns([failed]);

        var exceptions = await new ListRatingExceptionsHandler(_repository, _shippers).Handle(new ListRatingExceptionsQuery(), CancellationToken.None);

        exceptions.Should().ContainSingle().Which.Should().BeEquivalentTo(new RatingExceptionSummary(
            failed.Id.Value,
            "TO-0000004",
            new ShipperView("SHP-0001", "Brouwer Dranken"),
            "BD-2026-0001",
            Samples.Now.AddDays(1),
            Samples.Now,
            new RatingFailureView("NoRateCard", "Shipper SHP-0001 has no Rate Card."),
            Samples.Now.AddSeconds(2)));
    }

    [Fact]
    public async Task List_Rejected_ReturnsSummariesWithoutANumber()
    {
        var rejected = Samples.RejectedOrder();
        _repository.ListAsync(Arg.Is<TransportOrdersByStatusSpec>(s => s.Status == TransportOrderStatus.Rejected), Arg.Any<CancellationToken>())
            .Returns([rejected]);

        var summaries = await new ListTransportOrdersHandler(_repository, _shippers).Handle(new ListTransportOrdersQuery(TransportOrderStatus.Rejected), CancellationToken.None);

        summaries.Should().ContainSingle().Which.Should().BeEquivalentTo(new TransportOrderSummary(
            rejected.Id.Value,
            null,
            TransportOrderStatus.Rejected,
            null,
            new ShipperView("SHP-0001", "Brouwer Dranken"),
            "BD-2026-0001",
            Samples.Now.AddDays(1),
            Samples.Now.AddDays(1).AddHours(4),
            Samples.Now));
    }

    [Fact]
    public async Task List_ByStatus_ReturnsSummariesInTheOrderTheyWereReceived()
    {
        var first = Samples.AcceptedOrder(sequence: 1);
        var second = Samples.AcceptedOrder(sequence: 2);
        _repository.ListAsync(Arg.Is<TransportOrdersByStatusSpec>(s => s.Status == TransportOrderStatus.Accepted), Arg.Any<CancellationToken>())
            .Returns([first, second]);

        var summaries = await new ListTransportOrdersHandler(_repository, _shippers).Handle(new ListTransportOrdersQuery(TransportOrderStatus.Accepted), CancellationToken.None);

        summaries.Select(s => s.TransportOrderNumber).Should().Equal("TO-0000001", "TO-0000002");
        summaries[0].Should().BeEquivalentTo(new TransportOrderSummary(
            first.Id.Value,
            "TO-0000001",
            TransportOrderStatus.Accepted,
            RatingResult.NotYetRated,
            new ShipperView("SHP-0001", "Brouwer Dranken"),
            "BD-2026-0001",
            Samples.Now.AddDays(1),
            Samples.Now.AddDays(1).AddHours(4),
            Samples.Now));
    }

    [Fact]
    public async Task List_Accepted_ShowsEachOrdersRatingResult_RatingFailedIncluded()
    {
        _repository.ListAsync(Arg.Is<TransportOrdersByStatusSpec>(s => s.Status == TransportOrderStatus.Accepted), Arg.Any<CancellationToken>())
            .Returns([Samples.AcceptedOrder(sequence: 1), Samples.RatedOrder(sequence: 2), Samples.RatingFailedOrder(sequence: 3)]);

        var summaries = await new ListTransportOrdersHandler(_repository, _shippers).Handle(new ListTransportOrdersQuery(TransportOrderStatus.Accepted), CancellationToken.None);

        summaries.Select(s => (s.TransportOrderNumber, s.RatingResult)).Should().Equal(
            ("TO-0000001", RatingResult.NotYetRated),
            ("TO-0000002", RatingResult.Rated),
            ("TO-0000003", RatingResult.RatingFailed));
    }

    [Fact]
    public async Task List_ShipperNotInSeed_ShowsTheShipperIdWithoutAName()
    {
        _shippers.Find(Arg.Any<ShipperId>()).Returns((Shipper?)null);
        _repository.ListAsync(Arg.Any<TransportOrdersByStatusSpec>(), Arg.Any<CancellationToken>()).Returns([Samples.AcceptedOrder()]);

        var summaries = await new ListTransportOrdersHandler(_repository, _shippers).Handle(new ListTransportOrdersQuery(TransportOrderStatus.Accepted), CancellationToken.None);

        summaries.Single().Shipper.Should().Be(new ShipperView("SHP-0001", null));
    }
}
