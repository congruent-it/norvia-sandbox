using Norvia.Tms.Domain.Fleet;
using Norvia.Tms.Domain.Trips;

namespace Norvia.Tms.Application.Trips;

internal static class TripViews
{
    /// <summary>
    /// A Trip for the Planner. A Draft may hold a Vehicle or Driver that has since gone inactive: it is still named, by its ID, and has
    /// no capacity to compare the load with.
    /// </summary>
    public static TripView Of(Trip trip, IReadOnlyList<ShipmentOnTrip> shipments, IReadOnlyDictionary<VehicleId, Vehicle> vehicles, IReadOnlyDictionary<DriverId, string> drivers, TripNeeds needs = default)
    {
        var load = LoadCheck.Sum(shipments.Select(shipment => shipment.Load));
        var vehicle = trip.VehicleId is { } id ? vehicles.GetValueOrDefault(id) : null;
        return new TripView(
            trip.Number.Value,
            trip.Day,
            trip.Status,
            trip.VehicleId?.Value,
            trip.VehicleId is { } vehicleId ? vehicle?.Registration ?? vehicleId.Value : null,
            trip.DriverId?.Value,
            trip.DriverId is { } driver ? drivers.GetValueOrDefault(driver, driver.Value) : null,
            trip.PlannedStart,
            trip.PlannedDrivingDuration,
            trip.Stops.Count,
            load.WeightKg,
            load.PalletPlaces,
            vehicle?.PayloadKg,
            vehicle?.PalletPlaces,
            trip.LatestDelivery?.State,
            needs.Driver,
            needs.Vehicle);
    }
}
