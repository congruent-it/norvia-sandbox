using System.Globalization;
using Norvia.Tms.Application.Trips;
using Norvia.Tms.Application.Trips.GetByNumber;
using Norvia.Tms.Domain.Trips;

namespace Norvia.Tms.Web.Trips;

/// <summary>How the Planner's Trip views word the Stops and the load.</summary>
public static class TripText
{
    /// <summary>The load against the Vehicle's capacity, such as "650 / 1,200 kg · 3 / 4 pallets"; just the load while the Trip has no Vehicle.</summary>
    public static string Load(TripView trip)
    {
        ArgumentNullException.ThrowIfNull(trip);
        return Load(trip.LoadWeightKg, trip.LoadPalletPlaces, trip.PayloadKg, trip.VehiclePalletPlaces);
    }

    public static string Load(decimal weightKg, int palletPlaces, int? payloadKg, int? vehiclePalletPlaces) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"{weightKg:N0}{(payloadKg is { } payload ? $" / {payload:N0}" : string.Empty)} kg · {palletPlaces}{(vehiclePalletPlaces is { } places ? $" / {places}" : string.Empty)} pallets");

    /// <summary>Whether the Driver app has a Trip version, as the Planner reads it; a dash while there is none to speak of (a Draft).</summary>
    public static string Delivery(TripDeliveryState? state) =>
        state switch
        {
            TripDeliveryState.Sending => "Sending",
            TripDeliveryState.Delivered => "Delivered to Driver app",
            TripDeliveryState.NotDelivered => "Not delivered",
            _ => "—",
        };

    public static string Kind(StopKind kind) => kind == StopKind.Collection ? "Collect" : "Deliver";

    /// <summary>Where a Stop is, led by who receives the goods at a delivery: "Café De Zwaan, Oudegracht 112, 3511 AW Utrecht".</summary>
    public static string Place(TripStopView stop)
    {
        ArgumentNullException.ThrowIfNull(stop);
        return stop.Name is { Length: > 0 } name ? $"{name}, {stop.Place}" : stop.Place;
    }
}
