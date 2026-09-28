using Norvia.Tms.Application.Shipments;
using Norvia.Tms.Application.TransportOrders;

namespace Norvia.Tms.Web.TransportOrders;

/// <summary>Words planning as a Planner reads it, in the glossary's terms.</summary>
public static class PlanningText
{
    public const string OwnFleet = "Own fleet";

    /// <summary>A place at a glance, such as "Rotterdam, NL": the city, or the postcode when no city was given.</summary>
    public static string Place(PlaceView? place)
    {
        var town = string.IsNullOrWhiteSpace(place?.City) ? place?.Postcode : place.City;
        var parts = new[] { town, place?.Country }.Where(part => !string.IsNullOrWhiteSpace(part)).ToArray();
        return parts.Length == 0 ? NorviaTime.NotGiven : string.Join(", ", parts);
    }

    /// <summary>Who carries a Shipment: Own fleet, or the Carrier by name, or by ID when the TMS doesn't know it.</summary>
    public static string CarriedBy(CarriedByView carriedBy)
    {
        ArgumentNullException.ThrowIfNull(carriedBy);
        return carriedBy.Carrier is { } carrier ? Carrier(carrier) : OwnFleet;
    }

    public static string Carrier(CarrierView carrier)
    {
        ArgumentNullException.ThrowIfNull(carrier);
        return carrier.Name ?? carrier.Id;
    }
}
