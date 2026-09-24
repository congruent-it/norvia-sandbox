using Ardalis.Result;
using Mediator;
using Microsoft.Extensions.DependencyInjection;
using Norvia.Tms.Application.TransportOrders;
using Norvia.Tms.Application.TransportOrders.GetByNumber;
using Norvia.Tms.Application.TransportOrders.List;
using Norvia.Tms.Domain.TransportOrders;
using Norvia.Tms.Web.TransportOrders;

namespace Norvia.Tms.Web.UnitTests.TransportOrders;

public sealed class TransportOrderQueriesTests : IDisposable
{
    private readonly IMediator _mediator = Substitute.For<IMediator>();
    private readonly ServiceProvider _services;
    private readonly TransportOrderQueries _queries;

    public TransportOrderQueriesTests()
    {
        _services = new ServiceCollection().AddScoped(_ => _mediator).BuildServiceProvider();
        _queries = new TransportOrderQueries(_services.GetRequiredService<IServiceScopeFactory>());
    }

    [Fact]
    public async Task ListWaitingForPlanningAsync_ListsTheAcceptedTransportOrders()
    {
        IReadOnlyList<TransportOrderSummary> accepted = [Samples.Summary()];
        _mediator.Send(new ListTransportOrdersQuery(TransportOrderStatus.Accepted), Arg.Any<CancellationToken>()).Returns(accepted);

        var orders = await _queries.ListWaitingForPlanningAsync(Xunit.TestContext.Current.CancellationToken);

        orders.Should().BeSameAs(accepted);
    }

    [Fact]
    public async Task FindAsync_AKnownNumber_ReturnsTheTransportOrder()
    {
        var view = Samples.View("TO-0000042");
        _mediator.Send(new GetTransportOrderQuery("TO-0000042"), Arg.Any<CancellationToken>()).Returns(Result.Success(view));

        var order = await _queries.FindAsync("TO-0000042", Xunit.TestContext.Current.CancellationToken);

        order.Should().BeSameAs(view);
    }

    [Fact]
    public async Task FindAsync_AnUnknownNumber_ReturnsNull()
    {
        _mediator.Send(new GetTransportOrderQuery("TO-0000999"), Arg.Any<CancellationToken>()).Returns(Result<TransportOrderView>.NotFound());

        var order = await _queries.FindAsync("TO-0000999", Xunit.TestContext.Current.CancellationToken);

        order.Should().BeNull();
    }

    public void Dispose() => _services.Dispose();
}
