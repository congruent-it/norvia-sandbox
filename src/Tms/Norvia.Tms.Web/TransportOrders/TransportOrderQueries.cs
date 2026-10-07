using Ardalis.Result;
using Mediator;
using Norvia.Tms.Application.Carriers.ListActive;
using Norvia.Tms.Application.Exceptions.ListOpen;
using Norvia.Tms.Application.Exceptions.ShipperNotificationTemplate;
using Norvia.Tms.Application.ProofsOfDelivery.ListMissing;
using Norvia.Tms.Application.ProofsOfDelivery.Read;
using Norvia.Tms.Application.Shipments;
using Norvia.Tms.Application.Shipments.GetByNumber;
using Norvia.Tms.Application.Shipments.ListAwaitingAnswer;
using Norvia.Tms.Application.Shipments.ListInProgress;
using Norvia.Tms.Application.Shipments.ListToRelease;
using Norvia.Tms.Application.TrackingEvents.Timeline;
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

    public Task<IReadOnlyList<CarrierBookingAwaitingAnswerSummary>> ListCarrierBookingsAwaitingAnswerAsync(CancellationToken cancellationToken) =>
        SendAsync(new ListCarrierBookingsAwaitingAnswerQuery(), cancellationToken);

    public Task<IReadOnlyList<ShipmentInProgressSummary>> ListShipmentsInProgressAsync(CancellationToken cancellationToken) =>
        SendAsync(new ListShipmentsInProgressQuery(), cancellationToken);

    public Task<IReadOnlyList<DeliveredShipmentWithoutProofOfDeliverySummary>> ListDeliveredShipmentsWithoutProofOfDeliveryAsync(CancellationToken cancellationToken) =>
        SendAsync(new ListDeliveredShipmentsWithoutProofOfDeliveryQuery(), cancellationToken);

    public async Task<ShipmentTimeline?> FindTrackingTimelineAsync(string shipmentNumber, CancellationToken cancellationToken) =>
        FoundOrNull(await SendAsync(new GetTrackingTimelineQuery(shipmentNumber), cancellationToken), $"the timeline of Shipment {shipmentNumber}");

    public Task<IReadOnlyList<OpenExceptionSummary>> ListOpenExceptionsAsync(CancellationToken cancellationToken) =>
        SendAsync(new ListOpenExceptionsQuery(), cancellationToken);

    public async Task<string?> FindShipperNotificationTemplateAsync(string exceptionNumber, CancellationToken cancellationToken) =>
        (await SendAsync(new ShipperNotificationTemplateQuery(exceptionNumber), cancellationToken)) switch
        {
            { IsSuccess: true } result => result.Value.Message,
            { Status: ResultStatus.NotFound or ResultStatus.Invalid } => null,
            var result => throw new InvalidOperationException($"Reading the template of Exception {exceptionNumber} failed with {result.Status}."),
        };

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
