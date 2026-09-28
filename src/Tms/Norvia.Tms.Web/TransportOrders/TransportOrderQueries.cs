using Ardalis.Result;
using Mediator;
using Norvia.Tms.Application.Carriers.ListActive;
using Norvia.Tms.Application.Shipments;
using Norvia.Tms.Application.Shipments.GetByNumber;
using Norvia.Tms.Application.Shipments.ListToRelease;
using Norvia.Tms.Application.TransportOrders;
using Norvia.Tms.Application.TransportOrders.GetByNumber;
using Norvia.Tms.Application.TransportOrders.GetRejectedBooking;
using Norvia.Tms.Application.TransportOrders.List;
using Norvia.Tms.Application.TransportOrders.RatingExceptions;
using Norvia.Tms.Application.TransportOrders.ToPlan;
using Norvia.Tms.Domain.TransportOrders;

namespace Norvia.Tms.Web.TransportOrders;

/// <summary>
/// Sends the TMS queries, each in a scope of its own. A Blazor Server circuit lives as long as the page is open and
/// the live list reads while the Planner clicks, so a scope per circuit would share one database context between them.
/// </summary>
internal sealed class TransportOrderQueries(IServiceScopeFactory scopes) : ITransportOrderQueries
{
    public Task<IReadOnlyList<TransportOrderToPlanSummary>> ListWaitingForPlanningAsync(CancellationToken cancellationToken) =>
        SendAsync(new ListTransportOrdersToPlanQuery(), cancellationToken);

    public Task<IReadOnlyList<TransportOrderSummary>> ListRejectedAsync(CancellationToken cancellationToken) =>
        SendAsync(new ListTransportOrdersQuery(TransportOrderStatus.Rejected), cancellationToken);

    public Task<IReadOnlyList<RatingExceptionSummary>> ListRatingExceptionsAsync(CancellationToken cancellationToken) =>
        SendAsync(new ListRatingExceptionsQuery(), cancellationToken);

    public Task<IReadOnlyList<CarrierView>> ListActiveCarriersAsync(CancellationToken cancellationToken) =>
        SendAsync(new ListActiveCarriersQuery(), cancellationToken);

    public async Task<TransportOrderView?> FindAsync(string transportOrderNumber, CancellationToken cancellationToken) =>
        FoundOrNull(await SendAsync(new GetTransportOrderQuery(transportOrderNumber), cancellationToken), $"Transport Order {transportOrderNumber}");

    public async Task<TransportOrderView?> FindRejectedAsync(Guid id, CancellationToken cancellationToken) =>
        FoundOrNull(await SendAsync(new GetRejectedBookingQuery(id), cancellationToken), $"Rejected booking {id}");

    public async Task<ShipmentView?> FindShipmentAsync(string shipmentNumber, CancellationToken cancellationToken) =>
        FoundOrNull(await SendAsync(new GetShipmentQuery(shipmentNumber), cancellationToken), $"Shipment {shipmentNumber}");

    public Task<IReadOnlyList<ShipmentToReleaseSummary>> ListShipmentsToReleaseAsync(CancellationToken cancellationToken) =>
        SendAsync(new ListShipmentsToReleaseQuery(), cancellationToken);

    private async Task<TResponse> SendAsync<TResponse>(IQuery<TResponse> query, CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IMediator>().Send(query, cancellationToken);
    }

    private static TView? FoundOrNull<TView>(Result<TView> result, string sought)
        where TView : class => result.Status switch
        {
            ResultStatus.Ok => result.Value,
            ResultStatus.NotFound => null,
            _ => throw new InvalidOperationException($"Reading {sought} failed with {result.Status}."),
        };
}
