using Mediator;
using Norvia.Tms.Domain.ShipperTracking;

namespace Norvia.Tms.Application.ShipperTracking.ListTransportOrders;

/// <param name="ShipperId">The Shipper whose Transport Orders are listed, from the caller's header: every operation is scoped to it.</param>
/// <param name="TrackingStatus">Only the Transport Orders with this Tracking status; null for all.</param>
/// <param name="CollectionFrom">Only the Transport Orders whose collection window opens on or after this date; null for no lower bound.</param>
/// <param name="CollectionTo">Only the Transport Orders whose collection window opens on or before this date; null for no upper bound.</param>
/// <param name="Page">The page to return, counted from 1.</param>
/// <param name="PageSize">The number of Transport Orders on a page.</param>
public sealed record ListShipperTransportOrdersQuery(
    string ShipperId,
    TrackingStatus? TrackingStatus = null,
    DateOnly? CollectionFrom = null,
    DateOnly? CollectionTo = null,
    int Page = 1,
    int PageSize = ListShipperTransportOrdersQuery.DefaultPageSize) : IQuery<IReadOnlyList<ShipperTransportOrderListItem>>
{
    public const int DefaultPageSize = 25;
    public const int MaxPageSize = 100;
}
