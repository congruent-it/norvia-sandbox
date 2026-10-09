using Ardalis.SharedKernel;
using Norvia.Tms.Domain.Fleet;
using Norvia.Tms.Domain.RateCards;
using Norvia.Tms.Domain.Shipments;
using Norvia.Tms.Domain.TransportOrders;

namespace Norvia.Tms.Application.Fleet.Planning;

/// <summary>The load check's figures (P10), shared by the Capacity view and the warning P3 shows when it assigns Own fleet work.</summary>
public sealed class DayLoads(
    IReadRepository<Shipment> shipments,
    IReadRepository<TransportOrder> transportOrders,
    IDriverDirectory drivers,
    IVehicleDirectory vehicles)
{
    /// <summary>The Own fleet's demand per collection day: the Planned and Released Own fleet Shipments, on the Europe/Amsterdam date of their collection window's earliest time.</summary>
    public async Task<IReadOnlyDictionary<DateOnly, FleetLoad>> DemandByDayAsync(CancellationToken cancellationToken)
    {
        var inDemand = await shipments.ListAsync(new OwnFleetShipmentsInDemandSpec(), cancellationToken);
        if (inDemand.Count == 0)
        {
            return new Dictionary<DateOnly, FleetLoad>();
        }

        var orders = await transportOrders.ListAsync(new TransportOrdersByIdsSpec([.. inDemand.Select(shipment => shipment.TransportOrderId).Distinct()]), cancellationToken);
        var detailsById = orders.ToDictionary(order => order.Id, order => order.Details);
        return inDemand
            .Select(shipment => detailsById.GetValueOrDefault(shipment.TransportOrderId))
            .OfType<TransportOrderDetails>()
            .Select(details => (Day: CollectionDate.Of(details), Load: LoadCheck.DemandOf(details)))
            .Where(entry => entry.Day is not null)
            .GroupBy(entry => entry.Day!.Value)
            .ToDictionary(group => group.Key, group => LoadCheck.Sum(group.Select(entry => entry.Load)));
    }

    /// <summary>The day's capacity from its active Drivers' Shifts and active available Vehicles, against the demand.</summary>
    public DayLoadView Of(PlanningDay day, IReadOnlyDictionary<DateOnly, FleetLoad> demandByDay) =>
        LoadOf(day, demandByDay, drivers.ListActive().ToDictionary(driver => driver.Id), vehicles.ListActive().ToDictionary(vehicle => vehicle.Id));

    /// <summary>As above, for many days at once, with the active Drivers and Vehicles looked up once.</summary>
    public static DayLoadView LoadOf(
        PlanningDay day,
        IReadOnlyDictionary<DateOnly, FleetLoad> demandByDay,
        IReadOnlyDictionary<DriverId, Driver> activeDrivers,
        IReadOnlyDictionary<VehicleId, Vehicle> activeVehicles)
    {
        ArgumentNullException.ThrowIfNull(day);
        ArgumentNullException.ThrowIfNull(demandByDay);
        ArgumentNullException.ThrowIfNull(activeDrivers);
        ArgumentNullException.ThrowIfNull(activeVehicles);
        var capacity = LoadCheck.CapacityOf(
            day.AvailableVehicles.Where(activeVehicles.ContainsKey).Select(id => activeVehicles[id]),
            day.Shifts.Where(shift => activeDrivers.ContainsKey(shift.DriverId)).Select(shift => activeDrivers[shift.DriverId]));
        var load = demandByDay.GetValueOrDefault(day.Date, FleetLoad.None);
        return new DayLoadView(capacity.WeightKg, capacity.PalletPlaces, load.WeightKg, load.PalletPlaces, load.Exceeds(capacity));
    }
}
