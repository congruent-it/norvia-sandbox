using Ardalis.Result;
using Ardalis.SharedKernel;
using Microsoft.Extensions.Time.Testing;
using Norvia.Tms.Application.RateCards;
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

        result.Value.Should().Be(RatingResult.Rated);
        order.FreightCharge!.Total.Should().Be(112.90m);
        order.RatedAt.Should().Be(Samples.Now.AddSeconds(5));
        await _repository.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ShipperWithoutARateCard_LeavesItNotYetRated()
    {
        var order = Samples.AcceptedOrder(sequence: 7);
        _rateCards.FindFor(new ShipperId("SHP-0001")).Returns((Domain.RateCards.RateCard?)null);
        _repository.SingleOrDefaultAsync(Arg.Any<TransportOrderToRateSpec>(), Arg.Any<CancellationToken>()).Returns(order);

        var result = await _handler.Handle(new RateTransportOrderCommand("TO-0000007"), CancellationToken.None);

        result.Value.Should().Be(RatingResult.NotYetRated);
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
