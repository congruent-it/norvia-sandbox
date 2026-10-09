using Ardalis.SharedKernel;
using Mediator;
using Norvia.Tms.Domain.Fleet;
using Norvia.Tms.Domain.RateCards;
using Norvia.Tms.Domain.Shipments;
using Norvia.Tms.Domain.TransportOrders;

namespace Norvia.Tms.Application.Fleet.Planning.ListOpenDays;

public sealed class ListOpenPlanningDaysHandler(
    IReadRepository<PlanningDay> days,
    IReadRepository<Shipment> shipments,
    IReadRepository<TransportOrder> transportOrders,
    IDriverDirectory drivers,
    IVehicleDirectory vehicles)
    : IQueryHandler<ListOpenPlanningDaysQuery, IReadOnlyList<PlanningDayView>>
{
    public async ValueTask<IReadOnlyList<PlanningDayView>> Handle(ListOpenPlanningDaysQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var activeDrivers = drivers.ListActive().ToDictionary(driver => driver.Id);
        var activeVehicles = vehicles.ListActive().ToDictionary(vehicle => vehicle.Id);
        var openDays = await days.ListAsync(new OpenPlanningDaysSpec(), cancellationToken);
        var demand = await DemandByDayAsync(cancellationToken);

        return
        [
            .. openDays.Select(day =>
            {
                var shifts = day.Shifts.Where(shift => activeDrivers.ContainsKey(shift.DriverId)).ToList();
                var capacity = LoadCheck.CapacityOf(
                    day.AvailableVehicles.Where(activeVehicles.ContainsKey).Select(id => activeVehicles[id]),
                    shifts.Select(shift => activeDrivers[shift.DriverId]));
                var load = demand.GetValueOrDefault(day.Date, FleetLoad.None);
                return new PlanningDayView(
                    day.Date,
                    day.AvailableVehicles.Count(activeVehicles.ContainsKey),
                    [
                        .. shifts
                            .Select(shift => new ShiftView(shift.DriverId.Value, activeDrivers[shift.DriverId].Name, shift.Start, shift.End, shift.EditedByHand))
                            .OrderBy(shift => shift.Start).ThenBy(shift => shift.DriverName, StringComparer.Ordinal),
                    ],
                    new DayLoadView(capacity.WeightKg, capacity.PalletPlaces, load.WeightKg, load.PalletPlaces, load.Exceeds(capacity)));
            }),
        ];
    }

    /// <summary>The Own fleet's demand per collection day: the Planned and Released Own fleet Shipments, on the Europe/Amsterdam date of their collection window's earliest time.</summary>
    private async Task<Dictionary<DateOnly, FleetLoad>> DemandByDayAsync(CancellationToken cancellationToken)
    {
        var inDemand = await shipments.ListAsync(new OwnFleetShipmentsInDemandSpec(), cancellationToken);
        if (inDemand.Count == 0)
        {
            return [];
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
}
