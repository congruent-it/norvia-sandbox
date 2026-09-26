using Ardalis.SharedKernel;
using Norvia.Tms.Application.TransportOrders.Rate;
using Norvia.Tms.Domain.TransportOrders;
using NSubstitute;

namespace Norvia.Tms.Application.UnitTests.TransportOrders;

public sealed class ListTransportOrdersNotYetRatedTests
{
    private readonly IReadRepository<TransportOrder> _repository = Substitute.For<IReadRepository<TransportOrder>>();

    [Fact]
    public async Task Handle_OrdersWaitingForRating_ListsThemWithTheirTrailKeys()
    {
        _repository.ListAsync(Arg.Is<TransportOrdersNotYetRatedSpec>(_ => true), Arg.Any<CancellationToken>())
            .Returns([Samples.AcceptedOrder(sequence: 4, correlationId: "corr-4"), Samples.AcceptedOrder(sequence: 5, correlationId: "corr-5")]);

        var listed = await new ListTransportOrdersNotYetRatedHandler(_repository).Handle(new ListTransportOrdersNotYetRatedQuery(BatchSize: 10), CancellationToken.None);

        listed.Should().Equal(
            new TransportOrderToRate("TO-0000004", "SHP-0001", "corr-4"),
            new TransportOrderToRate("TO-0000005", "SHP-0001", "corr-5"));
    }
}
