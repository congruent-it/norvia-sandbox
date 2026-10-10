using Norvia.Tms.Domain.TransportOrders;
using Norvia.Tms.Domain.Trips;

namespace Norvia.Tms.Application.Trips.GetByNumber;

internal static class StopText
{
    public static TripStopView ViewOf(TripStop stop, ShipmentOnTrip item)
    {
        var details = item.Details;
        var isCollection = stop.Kind == StopKind.Collection;
        var window = isCollection ? details.Collection?.Window : item.DeliveryWindow;
        return new TripStopView(
            stop.Position,
            stop.Kind,
            item.Shipment.Number.Value,
            item.Shipment.TransportOrderNumber.Value,
            PlaceOf(isCollection ? details.Collection?.Address : details.Delivery?.Address),
            isCollection ? null : details.Delivery?.Consignee?.Name,
            window?.Earliest,
            window?.Latest,
            GoodsOf(details));
    }

    /// <summary>The street, then the postcode and city: "Oudegracht 112, 3511 AW Utrecht".</summary>
    public static string PlaceOf(Address? address)
    {
        if (address is null)
        {
            return "—";
        }

        var town = string.Join(' ', new[] { address.Postcode, address.City }.Where(part => !string.IsNullOrWhiteSpace(part)));
        var place = string.Join(", ", new[] { address.Street, town }.Where(part => !string.IsNullOrWhiteSpace(part)));
        return place.Length == 0 ? "—" : place;
    }

    /// <summary>The goods lines in short: "2 pallet, 4 crate".</summary>
    public static string GoodsOf(TransportOrderDetails details) =>
        string.Join(", ", details.GoodsLines.Select(line => FormattableString.Invariant($"{line.Quantity} {line.PackageType}").Trim()));
}
