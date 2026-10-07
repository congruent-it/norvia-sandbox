using Mediator;

namespace Norvia.Tms.Application.ShipperTracking.ListTransportOrders;

/// <param name="ShipperId">The Shipper whose Transport Orders are listed, from the caller's header: every operation is scoped to it.</param>
public sealed record ListShipperTransportOrdersQuery(string ShipperId) : IQuery<IReadOnlyList<ShipperTransportOrderListItem>>;
