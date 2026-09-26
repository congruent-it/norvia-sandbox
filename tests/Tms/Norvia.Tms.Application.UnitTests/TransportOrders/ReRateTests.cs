using Ardalis.Result;
using Ardalis.SharedKernel;
using Microsoft.Extensions.Time.Testing;
using Norvia.Tms.Application.RateCards;
using Norvia.Tms.Application.TransportOrders;
using Norvia.Tms.Application.TransportOrders.Rate;
using Norvia.Tms.Domain.Shippers;
using Norvia.Tms.Domain.TransportOrders;
using NSubstitute;

namespace Norvia.Tms.Application.UnitTests.TransportOrders;

/// <summary>Re-rating through the one "rate Transport Order" operation, as Customer service does from the TMS API.</summary>
public sealed class ReRateTests
{
    private readonly IRepository<TransportOrder> _repository = Substitute.For<IRepository<TransportOrder>>();
    private readonly IRateCardDirectory _rateCards = Substitute.For<IRateCardDirectory>();
    private readonly FakeTimeProvider _time = new(Samples.Now.AddHours(3));
    private readonly RateTransportOrderHandler _handler;

    public ReRateTests()
    {
        _rateCards.FindFor(new ShipperId("SHP-0001")).Returns(Samples.RateCard());
        _handler = new RateTransportOrderHandler(_repository, _rateCards, _time);
    }

    [Fact]
    public async Task Handle_RatingFailedOrderTheRateCardNowCovers_RatesItAndSavesIt()
    {
        var order = Samples.RatingFailedOrder(sequence: 7);
        _repository.SingleOrDefaultAsync(Arg.Is<TransportOrderToRateSpec>(s => s.Number.Value == "TO-0000007"), Arg.Any<CancellationToken>())
            .Returns(order);

        var result = await _handler.Handle(new RateTransportOrderCommand("TO-0000007", ReRateKey: "key-1"), CancellationToken.None);

        result.Value.Should().Be(new RateTransportOrderResult(RatingResult.Rated, null, "SHP-0001", "corr-1"));
        order.FreightCharge!.Total.Should().Be(new Money(112.90m));
        order.RatedAt.Should().Be(Samples.Now.AddHours(3));
        order.DomainEvents.Should().ContainSingle().Which.Should().BeOfType<TransportOrderRatedEvent>();
        await _repository.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_RateCardStillCantPriceIt_SavesItAsRatingFailedAgain()
    {
        var order = Samples.RatingFailedOrder(sequence: 7);
        _rateCards.FindFor(new ShipperId("SHP-0001")).Returns((Domain.RateCards.RateCard?)null);
        _repository.SingleOrDefaultAsync(Arg.Any<TransportOrderToRateSpec>(), Arg.Any<CancellationToken>()).Returns(order);

        var result = await _handler.Handle(new RateTransportOrderCommand("TO-0000007", ReRateKey: "key-1"), CancellationToken.None);

        result.Value.Should().Be(new RateTransportOrderResult(
            RatingResult.RatingFailed,
            new RatingFailureView("NoRateCard", "Shipper SHP-0001 has no Rate Card."),
            "SHP-0001",
            "corr-1"));
        order.RatingFailedAt.Should().Be(Samples.Now.AddHours(3));
        order.DomainEvents.Should().ContainSingle().Which.Should().BeOfType<TransportOrderRatingFailedEvent>();
        await _repository.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    public static TheoryData<string> OrdersNotRatingFailed() => ["Rated", "NotYetRated", "NotRatedSeparately"];

    [Theory]
    [MemberData(nameof(OrdersNotRatingFailed))]
    public async Task Handle_OrderWhoseRatingDidNotFail_IsRefusedAsAConflict_AndSavesNothing(string ratingResult)
    {
        var order = ratingResult switch
        {
            "Rated" => Samples.RatedOrder(sequence: 7),
            "NotYetRated" => Samples.AcceptedOrder(sequence: 7),
            _ => Samples.AcceptedOrder(sequence: 7, channel: IntakeChannel.Wms),
        };
        order.ClearDomainEvents();
        var freightCharge = order.FreightCharge;
        _repository.SingleOrDefaultAsync(Arg.Any<TransportOrderToRateSpec>(), Arg.Any<CancellationToken>()).Returns(order);

        var result = await _handler.Handle(new RateTransportOrderCommand("TO-0000007", ReRateKey: "key-1"), CancellationToken.None);

        result.Status.Should().Be(ResultStatus.Conflict);
        result.Errors.Should().ContainSingle().Which.Should().Be(
            $"Transport Order TO-0000007 is {ratingResult}; only a Transport Order whose Rating failed can be re-rated.");
        order.RatingResult.Should().Be(Enum.Parse<RatingResult>(ratingResult));
        order.FreightCharge.Should().BeSameAs(freightCharge);
        order.DomainEvents.Should().BeEmpty();
        await _repository.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ReRateSentAgainUnderItsKey_AnswersAsBefore_AsARepeat_AndSavesNothing()
    {
        var order = Samples.RatingFailedOrder(sequence: 7);
        _repository.SingleOrDefaultAsync(Arg.Any<TransportOrderToRateSpec>(), Arg.Any<CancellationToken>()).Returns(order);
        await _handler.Handle(new RateTransportOrderCommand("TO-0000007", ReRateKey: "key-1"), CancellationToken.None);
        order.ClearDomainEvents();
        _repository.ClearReceivedCalls();

        var again = await _handler.Handle(new RateTransportOrderCommand("TO-0000007", ReRateKey: "key-1"), CancellationToken.None);

        again.Value.Should().Be(new RateTransportOrderResult(RatingResult.Rated, null, "SHP-0001", "corr-1", Repeat: true));
        order.DomainEvents.Should().BeEmpty();
        await _repository.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("TO-0000099")]
    [InlineData("42")]
    public async Task Handle_UnknownOrMalformedNumber_ReturnsNotFound(string number)
    {
        var result = await _handler.Handle(new RateTransportOrderCommand(number, ReRateKey: "key-1"), CancellationToken.None);

        result.Status.Should().Be(ResultStatus.NotFound);
    }

    [Fact]
    public async Task Handle_RejectedBooking_ReturnsNotFound()
    {
        _repository.SingleOrDefaultAsync(Arg.Any<TransportOrderToRateSpec>(), Arg.Any<CancellationToken>()).Returns(Samples.RejectedOrder());

        var result = await _handler.Handle(new RateTransportOrderCommand("TO-0000007", ReRateKey: "key-1"), CancellationToken.None);

        result.Status.Should().Be(ResultStatus.NotFound);
    }
}
