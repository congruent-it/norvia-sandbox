using Ardalis.Specification;
using Norvia.Tms.Domain.Shippers;

namespace Norvia.Tms.Domain.TransportOrders;

/// <summary>
/// The Transport Orders of the TMS's list, newest first, one page at a time: the Accepted and Planned ones, optionally of one
/// status, one Shipper and the given Transport Orders (those whose Shipment a Carrier or the Own fleet carries), and optionally those whose Transport Order number, Shipper reference or Shipment number contains the
/// search text, whatever its case. A Rejected booking has no number and is never listed.
/// </summary>
public sealed class TransportOrdersSearchSpec : Specification<TransportOrder>
{
    public TransportOrdersSearchSpec(TransportOrderStatus? status, ShipperId? shipperId, string? search, int page, int pageSize,
        IReadOnlyCollection<TransportOrderId>? onlyTransportOrders = null)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(page, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(pageSize, 1);

        Query.Where(order => order.Status != TransportOrderStatus.Rejected);
        if (status is { } only)
        {
            Query.Where(order => order.Status == only);
        }

        if (shipperId is not null)
        {
            Query.Where(order => order.ShipperId == shipperId);
        }

        if (onlyTransportOrders is not null)
        {
            Query.Where(order => onlyTransportOrders.Contains(order.Id));
        }

        if (NormalizeSearch(search) is { } term)
        {
            // Upper-cased on both sides: the Shipper reference's column compares bytes, so a plain Contains would be case-sensitive.
            // EF Core translates neither Contains with a StringComparison (CA1862) nor ToUpperInvariant (CA1304, CA1311), only ToUpper, which
            // SQL Server runs as UPPER, whatever the host's culture.
#pragma warning disable CA1862, CA1304, CA1311
            Query.Where(order =>
                order.NumberText!.ToUpper().Contains(term)
                || (order.ShipperReference != null && order.ShipperReference.ToUpper().Contains(term))
                || (order.ShipmentNumberText != null && order.ShipmentNumberText.ToUpper().Contains(term)));
#pragma warning restore CA1862, CA1304, CA1311
        }

        Query.OrderByDescending(order => order.ReceivedAt)
            .ThenByDescending(order => order.NumberText)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .AsNoTracking();
    }

    /// <summary>The search text as it matches: trimmed and upper-cased; null for none, which filters nothing.</summary>
    public static string? NormalizeSearch(string? search) =>
        string.IsNullOrWhiteSpace(search) ? null : search.Trim().ToUpperInvariant();
}
