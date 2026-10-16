using Norvia.Simulator.Domain.Planner;
using Norvia.Simulator.Domain.Worlds;

namespace Norvia.Simulator.Domain.Trips;

/// <summary>
/// Builds the Trips of one day the way a careful Planner would, within the rules the TMS checks (P11): a Vehicle and a Driver have one Trip a day, the
/// Driver works that day and holds the licence for the Vehicle, the Shipments fit the Vehicle's payload and pallet places, the planned start is the
/// start of the Driver's Shift and the planned driving duration stays well inside the daily Driving-hour budget. The smallest Vehicle that carries a
/// Shipment is used, with the Driver whose licence is worth the least, so the big Vehicles and the best licences stay for what needs them. What the
/// TMS knows and the Simulator doesn't, such as an Absence or a Vehicle off the road, it refuses and the Planner logs.
/// </summary>
public static class TripBuilder
{
    private static readonly TimeSpan BaseDuration = TimeSpan.FromMinutes(60);
    private static readonly TimeSpan PerShipment = TimeSpan.FromMinutes(45);
    private static readonly TimeSpan LongestDuration = TimeSpan.FromHours(8);

    public static TripBuild Build(
        DateOnly day, IEnumerable<TripShipment> shipments, IReadOnlyList<SimulatedVehicle> vehicles, IReadOnlyList<SimulatedDriver> drivers, IReadOnlyList<DriverShift> shifts)
    {
        ArgumentNullException.ThrowIfNull(shipments);
        ArgumentNullException.ThrowIfNull(vehicles);
        ArgumentNullException.ThrowIfNull(drivers);
        ArgumentNullException.ThrowIfNull(shifts);

        var working = drivers.Where(driver => shifts.Any(shift => shift.DriverId == driver.Id)).ToList();
        var open = new List<OpenTrip>();
        var unplaced = new List<TripShipment>();

        foreach (var shipment in shipments.OrderBy(shipment => shipment.CollectionFrom).ThenBy(shipment => shipment.ShipmentNumber, StringComparer.Ordinal))
        {
            if (open.FirstOrDefault(trip => trip.Fits(shipment)) is { } room)
            {
                room.Shipments.Add(shipment);
                continue;
            }

            var free = Pair(shipment, vehicles, working, open);
            if (free is null)
            {
                unplaced.Add(shipment);
                continue;
            }

            open.Add(new OpenTrip(free.Value.Vehicle, free.Value.Driver) { Shipments = { shipment } });
        }

        return new TripBuild(
            [.. open.Select(trip => new TripDraft(
                day, trip.Vehicle, trip.Driver, shifts.First(shift => shift.DriverId == trip.Driver.Id).Start, DurationOf(trip.Shipments.Count), trip.Shipments))],
            unplaced);
    }

    public static TimeSpan DurationOf(int shipmentCount)
    {
        var duration = BaseDuration + (PerShipment * shipmentCount);
        return duration > LongestDuration ? LongestDuration : duration;
    }

    private static (SimulatedVehicle Vehicle, SimulatedDriver Driver)? Pair(
        TripShipment shipment, IReadOnlyList<SimulatedVehicle> vehicles, List<SimulatedDriver> working, List<OpenTrip> open)
    {
        foreach (var vehicle in vehicles
            .Where(vehicle => open.TrueForAll(trip => trip.Vehicle.Id != vehicle.Id) && FitsAlone(vehicle, shipment))
            .OrderBy(vehicle => vehicle.PayloadKg).ThenBy(vehicle => vehicle.Id, StringComparer.Ordinal))
        {
            var driver = working
                .Where(candidate => open.TrueForAll(trip => trip.Driver.Id != candidate.Id) && candidate.CanDrive(vehicle))
                .OrderBy(candidate => candidate.Highest).ThenBy(candidate => candidate.Id, StringComparer.Ordinal)
                .FirstOrDefault();
            if (driver is not null)
            {
                return (vehicle, driver);
            }
        }

        return null;
    }

    private static bool FitsAlone(SimulatedVehicle vehicle, TripShipment shipment) =>
        shipment.Load.WeightKg <= vehicle.PayloadKg && shipment.Load.PalletPlaces <= vehicle.PalletPlaces;

    private sealed class OpenTrip(SimulatedVehicle vehicle, SimulatedDriver driver)
    {
        public SimulatedVehicle Vehicle { get; } = vehicle;

        public SimulatedDriver Driver { get; } = driver;

        public List<TripShipment> Shipments { get; } = [];

        // The whole Trip counts as loaded at once (P10).
        public bool Fits(TripShipment shipment) =>
            Shipments.Sum(held => held.Load.WeightKg) + shipment.Load.WeightKg <= Vehicle.PayloadKg
            && Shipments.Sum(held => held.Load.PalletPlaces) + shipment.Load.PalletPlaces <= Vehicle.PalletPlaces;
    }
}
