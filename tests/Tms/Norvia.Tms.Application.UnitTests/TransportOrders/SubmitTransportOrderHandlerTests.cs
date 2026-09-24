using Ardalis.SharedKernel;
using Microsoft.Extensions.Time.Testing;
using Norvia.Tms.Application.TransportOrders;
using Norvia.Tms.Application.TransportOrders.Submit;
using Norvia.Tms.Domain.TransportOrders;
using NSubstitute;

namespace Norvia.Tms.Application.UnitTests.TransportOrders;

public sealed class SubmitTransportOrderHandlerTests
{
    private readonly IRepository<TransportOrder> _repository = Substitute.For<IRepository<TransportOrder>>();
    private readonly ITransportOrderNumbers _numbers = Substitute.For<ITransportOrderNumbers>();
    private readonly FakeTimeProvider _time = new(Samples.Now);
    private readonly SubmitTransportOrderHandler _handler;

    public SubmitTransportOrderHandlerTests()
    {
        _numbers.NextAsync(Arg.Any<CancellationToken>()).Returns(TransportOrderNumber.FromSequence(42));
        _handler = new SubmitTransportOrderHandler(_repository, _numbers, _time);
    }

    [Fact]
    public async Task Handle_ValidBooking_ReturnsAcceptedWithTheNextNumber()
    {
        var result = await _handler.Handle(new SubmitTransportOrderCommand(Samples.CanonicalTransportOrder(), "corr-1"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(new SubmitTransportOrderResult("TO-0000042", TransportOrderStatus.Accepted));
    }

    [Fact]
    public async Task Handle_ValidBooking_StoresTheAcceptedTransportOrder()
    {
        await _handler.Handle(new SubmitTransportOrderCommand(Samples.CanonicalTransportOrder(), "corr-1"), CancellationToken.None);

        await _repository.Received(1).AddAsync(
            Arg.Is<TransportOrder>(order =>
                order.Number.Value == "TO-0000042"
                && order.Status == TransportOrderStatus.Accepted
                && order.ShipperId.Value == "SHP-0001"
                && order.CorrelationId.Value == "corr-1"
                && order.ReceivedAt == Samples.Now
                && order.Details == Samples.CanonicalTransportOrder().ToDetails()),
            Arg.Any<CancellationToken>());
    }
}
