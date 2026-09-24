using Ardalis.Result;
using Mediator;
using Norvia.Tms.Application.TransportOrders;
using Norvia.Tms.Application.TransportOrders.GetByNumber;
using Norvia.Tms.Application.TransportOrders.List;
using Norvia.Tms.Domain.TransportOrders;

namespace Norvia.Tms.Web.TransportOrders;

/// <summary>
/// Sends the TMS queries, each in a scope of its own. A Blazor Server circuit lives as long as the page is open and
/// the live list reads while the Planner clicks, so a scope per circuit would share one database context between them.
/// </summary>
internal sealed class TransportOrderQueries(IServiceScopeFactory scopes) : ITransportOrderQueries
{
    public async Task<IReadOnlyList<TransportOrderSummary>> ListWaitingForPlanningAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IMediator>()
            .Send(new ListTransportOrdersQuery(TransportOrderStatus.Accepted), cancellationToken);
    }

    public async Task<TransportOrderView?> FindAsync(string transportOrderNumber, CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var result = await scope.ServiceProvider.GetRequiredService<IMediator>()
            .Send(new GetTransportOrderQuery(transportOrderNumber), cancellationToken);
        return result.Status switch
        {
            ResultStatus.Ok => result.Value,
            ResultStatus.NotFound => null,
            _ => throw new InvalidOperationException($"Reading Transport Order {transportOrderNumber} failed with {result.Status}."),
        };
    }
}
