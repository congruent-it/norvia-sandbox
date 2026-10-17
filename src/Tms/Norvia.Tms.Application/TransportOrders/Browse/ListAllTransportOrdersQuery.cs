using Mediator;
using Norvia.Tms.Domain.TransportOrders;

namespace Norvia.Tms.Application.TransportOrders.Browse;

/// <summary>
/// One page of the TMS's Transport Orders, newest first: the Accepted and Planned ones, which a Rejected booking is neither of.
/// </summary>
/// <param name="Status">Only the Transport Orders with this status; null for Accepted and Planned alike.</param>
/// <param name="ShipperId">Only the Transport Orders of this Shipper; null for all.</param>
/// <param name="Search">Only the Transport Orders whose number, Shipper reference or Shipment number contains this text, whatever its case; null or blank for all.</param>
/// <param name="Page">The page to return, counted from 1.</param>
/// <param name="PageSize">The number of Transport Orders on a page.</param>
public sealed record ListAllTransportOrdersQuery(
    TransportOrderStatus? Status = null,
    string? ShipperId = null,
    string? Search = null,
    int Page = 1,
    int PageSize = ListAllTransportOrdersQuery.DefaultPageSize) : IQuery<TransportOrderPage>
{
    public const int DefaultPageSize = 25;
    public const int MaxPageSize = 100;
}
