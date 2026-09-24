using Norvia.Tms.Application.Shippers;
using Norvia.Tms.Domain.Shippers;

namespace Norvia.Tms.Application.TransportOrders;

/// <param name="Name">Null when the Shipper isn't in the TMS's Shipper data.</param>
public sealed record ShipperView(string Id, string? Name)
{
    public static ShipperView Of(ShipperId id, IShipperDirectory shippers)
    {
        ArgumentNullException.ThrowIfNull(id);
        ArgumentNullException.ThrowIfNull(shippers);
        return new(id.Value, shippers.Find(id)?.Name);
    }
}
