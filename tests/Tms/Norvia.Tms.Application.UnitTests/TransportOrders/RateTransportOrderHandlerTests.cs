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

public sealed class RateTransportOrderHandlerTests
{
    private readonly IRepository<TransportOrder> _repository = Substitute.For<IRepository<TransportOrder>>();
    private readonly IRateCardDirectory _rateCards = Substitute.For<IRateCardDirectory>();
    private readonly FakeTimeProvider _time = new(Samples.Now.AddSeconds(5));
    private readonly RateTransportOrderHandler _handler;

    public RateTransportOrderHandlerTests()
    {
        _rateCards.FindFor(new ShipperId("SHP-0001")).Returns(Samples.RateCard());
        _handler = new RateTransportOrderHandler(_repository, _rateCards, _time);
    }

    [Fact]
    public async Task Handle_NotYetRatedOrderTheRateCardCovers_RatesItAndSavesIt()
    {
        var order = Samples.AcceptedOrder(sequence: 7);
        _repository.SingleOrDefaultAsync(Arg.Is<TransportOrderToRateSpec>(s => s.Number.Value == "TO-0000007"), Arg.Any<CancellationToken>())
            .Returns(order);

        var result = await _handler.Handle(new RateTransportOrderCommand("TO-0000007"), CancellationToken.None);

        result.Value.Should().Be(new RateTransportOrderResult(RatingResult.Rated, null, "SHP-0001", "corr-1"));
        order.FreightCharge!.Total.Should().Be(new Money(112.90m));
        order.RatedAt.Should().Be(Samples.Now.AddSeconds(5));
        await _repository.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ShipperWithoutARateCard_SavesItAsRatingFailed()
    {
        var order = Samples.AcceptedOrder(sequence: 7);
        _rateCards.FindFor(new ShipperId("SHP-0001")).Returns((Domain.RateCards.RateCard?)null);
        _repository.SingleOrDefaultAsync(Arg.Any<TransportOrderToRateSpec>(), Arg.Any<CancellationToken>()).Returns(order);

        var result = await _handler.Handle(new RateTransportOrderCommand("TO-0000007"), CancellationToken.None);

        result.Value.Should().Be(new RateTransportOrderResult(
            RatingResult.RatingFailed,
            new RatingFailureView("NoRateCard", "Shipper SHP-0001 has no Rate Card."),
            "SHP-0001",
            "corr-1"));
        order.RatingFailedAt.Should().Be(Samples.Now.AddSeconds(5));
        await _repository.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_OrderAlreadyRatingFailed_SavesNothing()
    {
        var order = Samples.RatingFailedOrder(sequence: 7);
        _repository.SingleOrDefaultAsync(Arg.Any<TransportOrderToRateSpec>(), Arg.Any<CancellationToken>()).Returns(order);

        var result = await _handler.Handle(new RateTransportOrderCommand("TO-0000007"), CancellationToken.None);

        result.Value.RatingResult.Should().Be(RatingResult.RatingFailed);
        await _repository.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("TO-0000099")]
    [InlineData("42")]
    public async Task Handle_UnknownOrMalformedNumber_ReturnsNotFound(string number)
    {
        var result = await _handler.Handle(new RateTransportOrderCommand(number), CancellationToken.None);

        result.Status.Should().Be(ResultStatus.NotFound);
    }
}
