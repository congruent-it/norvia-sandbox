using Ardalis.Result;
using Mediator;
using Microsoft.Extensions.DependencyInjection;
using Norvia.Tms.Application.TransportOrders;
using Norvia.Tms.Application.TransportOrders.GetById;
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

    [Fact]
    public async Task FindAsync_WhenTheQueryFails_ThrowsRatherThanSayingThereIsNone()
    {
        _mediator.Send(new GetTransportOrderQuery("TO-0000042"), Arg.Any<CancellationToken>()).Returns(Result<TransportOrderView>.Error("The query failed."));

        var find = () => _queries.FindAsync("TO-0000042", Xunit.TestContext.Current.CancellationToken);

        await find.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task ListRejectedAsync_ListsTheRejectedBookings()
    {
        IReadOnlyList<TransportOrderSummary> rejected = [Samples.RejectedSummary()];
        _mediator.Send(new ListTransportOrdersQuery(TransportOrderStatus.Rejected), Arg.Any<CancellationToken>()).Returns(rejected);

        var bookings = await _queries.ListRejectedAsync(Xunit.TestContext.Current.CancellationToken);

        bookings.Should().BeSameAs(rejected);
    }

    [Fact]
    public async Task FindRejectedAsync_ARejectedBooking_ReturnsIt()
    {
        var view = Samples.RejectedView();
        _mediator.Send(new GetTransportOrderByIdQuery(view.Id), Arg.Any<CancellationToken>()).Returns(Result.Success(view));

        var booking = await _queries.FindRejectedAsync(view.Id, Xunit.TestContext.Current.CancellationToken);

        booking.Should().BeSameAs(view);
    }

    [Fact]
    public async Task FindRejectedAsync_AnAcceptedTransportOrder_ReturnsNull()
    {
        var view = Samples.View("TO-0000042");
        _mediator.Send(new GetTransportOrderByIdQuery(view.Id), Arg.Any<CancellationToken>()).Returns(Result.Success(view));

        var booking = await _queries.FindRejectedAsync(view.Id, Xunit.TestContext.Current.CancellationToken);

        booking.Should().BeNull();
    }

    [Fact]
    public async Task FindRejectedAsync_AnUnknownId_ReturnsNull()
    {
        var id = Guid.NewGuid();
        _mediator.Send(new GetTransportOrderByIdQuery(id), Arg.Any<CancellationToken>()).Returns(Result<TransportOrderView>.NotFound());

        var booking = await _queries.FindRejectedAsync(id, Xunit.TestContext.Current.CancellationToken);

        booking.Should().BeNull();
    }

    [Fact]
    public async Task FindRejectedAsync_WhenTheQueryFails_ThrowsRatherThanSayingThereIsNone()
    {
        var id = Guid.NewGuid();
        _mediator.Send(new GetTransportOrderByIdQuery(id), Arg.Any<CancellationToken>()).Returns(Result<TransportOrderView>.Error("The query failed."));

        var find = () => _queries.FindRejectedAsync(id, Xunit.TestContext.Current.CancellationToken);

        await find.Should().ThrowAsync<InvalidOperationException>();
    }

    public void Dispose() => _services.Dispose();
}
