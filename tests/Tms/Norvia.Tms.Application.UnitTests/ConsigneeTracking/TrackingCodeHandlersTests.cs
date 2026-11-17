using Ardalis.Result;
using Ardalis.SharedKernel;
using Norvia.Tms.Application.ConsigneeTracking.GetCode;
using Norvia.Tms.Application.ConsigneeTracking.ReplaceCode;
using Norvia.Tms.Application.UnitTests.TransportOrders;
using Norvia.Tms.Domain.TransportOrders;
using NSubstitute;

namespace Norvia.Tms.Application.UnitTests.ConsigneeTracking;

/// <summary>The Planner reads and replaces a Transport Order's tracking code (ADR 0052).</summary>
public sealed class TrackingCodeHandlersTests
{
    private readonly IRepository<TransportOrder> _orders = Substitute.For<IRepository<TransportOrder>>();
    private readonly IReadRepository<TransportOrder> _reads = Substitute.For<IReadRepository<TransportOrder>>();

    [Fact]
    public async Task Get_ShowsTheCurrentCode()
    {
        var order = Samples.AcceptedOrder(sequence: 7);
        _reads.FirstOrDefaultAsync(Arg.Any<TransportOrderByNumberSpec>(), Arg.Any<CancellationToken>()).Returns(order);

        var result = await new GetTrackingCodeHandler(_reads).Handle(new GetTrackingCodeQuery("TO-0000007"), CancellationToken.None);

        result.Value.Should().Be(new Norvia.Tms.Application.ConsigneeTracking.TransportOrderTrackingCodeView("TO-0000007", order.TrackingCode!.Value));
    }

    [Theory]
    [InlineData("TO-0000099")]
    [InlineData("nonsense")]
    public async Task Get_UnknownTransportOrder_IsNotFound(string number)
    {
        _reads.FirstOrDefaultAsync(Arg.Any<TransportOrderByNumberSpec>(), Arg.Any<CancellationToken>()).Returns((TransportOrder?)null);

        var result = await new GetTrackingCodeHandler(_reads).Handle(new GetTrackingCodeQuery(number), CancellationToken.None);

        result.Status.Should().Be(ResultStatus.NotFound);
    }

    [Fact]
    public async Task Replace_GivesANewCode_AndSavesIt()
    {
        var order = Samples.AcceptedOrder(sequence: 7);
        var old = order.TrackingCode;
        _orders.FirstOrDefaultAsync(Arg.Any<TransportOrderToReplaceTrackingCodeSpec>(), Arg.Any<CancellationToken>()).Returns(order);

        var result = await new ReplaceTrackingCodeHandler(_orders).Handle(new ReplaceTrackingCodeCommand("TO-0000007"), CancellationToken.None);

        result.Value.TrackingCode.Should().Be(order.TrackingCode!.Value).And.NotBe(old!.Value);
        await _orders.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Replace_RejectedBooking_IsNotFound_AndSavesNothing()
    {
        _orders.FirstOrDefaultAsync(Arg.Any<TransportOrderToReplaceTrackingCodeSpec>(), Arg.Any<CancellationToken>()).Returns(Samples.RejectedOrder());

        var result = await new ReplaceTrackingCodeHandler(_orders).Handle(new ReplaceTrackingCodeCommand("TO-0000007"), CancellationToken.None);

        result.Status.Should().Be(ResultStatus.NotFound);
        await _orders.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Replace_UnknownTransportOrder_IsNotFound()
    {
        _orders.FirstOrDefaultAsync(Arg.Any<TransportOrderToReplaceTrackingCodeSpec>(), Arg.Any<CancellationToken>()).Returns((TransportOrder?)null);

        var result = await new ReplaceTrackingCodeHandler(_orders).Handle(new ReplaceTrackingCodeCommand("TO-0000099"), CancellationToken.None);

        result.Status.Should().Be(ResultStatus.NotFound);
    }
}
