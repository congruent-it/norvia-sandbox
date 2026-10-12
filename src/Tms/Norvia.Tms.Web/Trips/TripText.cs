using System.Globalization;
using Norvia.Tms.Application.Trips;
using Norvia.Tms.Application.Trips.GetByNumber;
using Norvia.Tms.Domain.Trips;
using Norvia.Tms.Web.TransportOrders;

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

    /// <summary>The P10 flags of a Trip: "Needs a Driver", "Needs a Vehicle", both, or a dash.</summary>
    public static string Flags(TripView trip)
    {
        ArgumentNullException.ThrowIfNull(trip);
        return Flags(trip.NeedsDriver, trip.NeedsVehicle);
    }

    public static string Flags(bool needsDriver, bool needsVehicle) =>
        (needsDriver, needsVehicle) switch
        {
            (true, true) => "Needs a Driver · Needs a Vehicle",
            (true, false) => "Needs a Driver",
            (false, true) => "Needs a Vehicle",
            _ => "—",
        };

    /// <summary>How many Stops the Trip visits, and once the Driver has started it how many of them have an outcome: "6" or "2 of 6 done".</summary>
    public static string Stops(TripView trip)
    {
        ArgumentNullException.ThrowIfNull(trip);
        return trip.Status >= TripStatus.InProgress ? string.Create(CultureInfo.InvariantCulture, $"{trip.StopsDone} of {trip.StopCount} done") : trip.StopCount.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>A Stop's outcome as the Driver reported it, with when it happened and their remark: "Delivery failed · 12 Oct 14:05 · Nobody home".</summary>
    public static string Outcome(TripStopView stop)
    {
        ArgumentNullException.ThrowIfNull(stop);
        if (stop.Outcome == StopOutcomeKind.Open)
        {
            return "Open";
        }

        var name = stop.Outcome switch
        {
            StopOutcomeKind.CollectionFailed => "Collection failed",
            StopOutcomeKind.DeliveryFailed => "Delivery failed",
            var other => other.ToString(),
        };
        return string.Join(" · ", new[] { name, stop.OutcomeAt is null ? null : NorviaTime.Format(stop.OutcomeAt), stop.OutcomeRemark }.Where(part => !string.IsNullOrWhiteSpace(part)));
    }

    public static string Kind(StopKind kind) => kind == StopKind.Collection ? "Collect" : "Deliver";

    /// <summary>Where a Stop is, led by who receives the goods at a delivery: "Café De Zwaan, Oudegracht 112, 3511 AW Utrecht".</summary>
    public static string Place(TripStopView stop)
    {
        ArgumentNullException.ThrowIfNull(stop);
        return stop.Name is { Length: > 0 } name ? $"{name}, {stop.Place}" : stop.Place;
    }
}
