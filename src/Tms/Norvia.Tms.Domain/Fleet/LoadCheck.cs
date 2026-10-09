using Norvia.Tms.Domain.TransportOrders;

namespace Norvia.Tms.Domain.Fleet;

/// <summary>
/// The load check (P10): a rough figure for the Planner, not a plan. It compares what the Own fleet can carry on a day with
/// the Own fleet work planned for it.
/// </summary>
public static class LoadCheck
{
    private const string PalletPackageType = "pallet";

    /// <summary>
    /// The day's capacity: the available Vehicles are paired with the available Drivers, the largest Vehicle first, each with
    /// the Driver with the lowest licence that covers it. Each paired Vehicle counts once (one Trip per Vehicle per day), so a
    /// Vehicle without a Driver adds nothing.
    /// </summary>
    public static FleetLoad CapacityOf(IEnumerable<Vehicle> availableVehicles, IEnumerable<Driver> availableDrivers)
    {
        ArgumentNullException.ThrowIfNull(availableVehicles);
        ArgumentNullException.ThrowIfNull(availableDrivers);
        var drivers = availableDrivers
            .OrderBy(driver => driver.Licences.Count == 0 ? 0 : driver.Licences.Max())
            .ThenBy(driver => driver.Name, StringComparer.Ordinal)
            .ToList();
        var capacity = FleetLoad.None;
        foreach (var vehicle in availableVehicles
            .OrderByDescending(vehicle => vehicle.PayloadKg)
            .ThenByDescending(vehicle => vehicle.PalletPlaces)
            .ThenBy(vehicle => vehicle.Registration, StringComparer.Ordinal))
        {
            var index = drivers.FindIndex(driver => driver.CanDrive(vehicle.Type));
            if (index < 0)
            {
                continue;
            }

            drivers.RemoveAt(index);
            capacity = new FleetLoad(capacity.WeightKg + vehicle.PayloadKg, capacity.PalletPlaces + vehicle.PalletPlaces);
        }

        return capacity;
    }

    /// <summary>
    /// What a Transport Order asks of the Own fleet: the weight of all its goods lines, and the pallet places, the quantity of
    /// its pallet goods lines. Parcels, crates and loose goods count only toward weight.
    /// </summary>
    public static FleetLoad DemandOf(TransportOrderDetails details)
    {
        ArgumentNullException.ThrowIfNull(details);
        var pallets = details.GoodsLines
            .Where(line => string.Equals(line.PackageType, PalletPackageType, StringComparison.OrdinalIgnoreCase))
            .Sum(line => line.Quantity ?? 0);
        return new FleetLoad(details.TotalWeightKg(), pallets);
    }

    public static FleetLoad Sum(IEnumerable<FleetLoad> loads)
    {
        ArgumentNullException.ThrowIfNull(loads);
        return loads.Aggregate(FleetLoad.None, (total, load) => new FleetLoad(total.WeightKg + load.WeightKg, total.PalletPlaces + load.PalletPlaces));
    }
}
