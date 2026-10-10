using Ardalis.SharedKernel;
using Norvia.Tms.Application.Fleet.Availability;
using Norvia.Tms.Domain.Fleet;
using Norvia.Tms.Domain.Shipments;
using Norvia.Tms.Domain.Trips;

namespace Norvia.Tms.Application.Trips;

/// <summary>
/// Applies P10's availability and load rules, and P11's rules about the planned start and the Shipments, to a Trip the Planner
/// wants to create or change. The load rules count the whole Trip as loaded at once. P11 owns Trips, so it tells P10 whether the Vehicle or Driver has another Trip that day: any Trip
/// of that day counts, Draft or Dispatched.
/// </summary>
public sealed class TripPlanningChecks(IReadRepository<Trip> trips, IReadRepository<PlanningDay> days, TripAvailabilityRules rules)
{
    /// <param name="current">The Trip being changed, which isn't its own "other Trip", and whose planned driving the new duration replaces.</param>
    /// <param name="shipments">Every Shipment the Trip would have: the ones on it already and the ones being added.</param>
    /// <param name="added">The Shipments being added, which must be Released, on no other Trip and collect on the Trip's day.</param>
    public async Task<TripPlanningCheck> CheckAsync(
        DateOnly day,
        VehicleId? vehicle,
        DriverId? driver,
        TimeOnly? start,
        TimeSpan? duration,
        Trip? current,
        IReadOnlyList<ShipmentOnTrip> shipments,
        IReadOnlyList<ShipmentOnTrip> added,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(shipments);
        ArgumentNullException.ThrowIfNull(added);
        var others = (await trips.ListAsync(new TripsOnDaySpec(day), cancellationToken)).Where(trip => trip.Number != current?.Number).ToList();
        var proposal = new TripProposal(
            day,
            LoadCheck.Sum(shipments.Select(shipment => shipment.Load)),
            vehicle,
            driver,
            duration,
            VehicleHasOtherTrip: vehicle is { } v && others.Vehicle(v),
            DriverHasOtherTrip: driver is { } d && others.Driver(d),
            PlannedDriving: current?.PlannedDriving);
        var failures = await rules.CheckAsync(proposal, cancellationToken);
        var refusals = new List<TripRefusal>(await CheckShiftAsync(day, driver, start, cancellationToken));
        refusals.AddRange(await CheckAddedAsync(day, added, current, cancellationToken));
        var warnings = shipments
            .Where(shipment => shipment.DeliversOutside(day))
            .Select(shipment => TripWarning.DeliveryWindowOutside(shipment.Shipment.Number.Value, day))
            .ToList();
        return new TripPlanningCheck(failures, refusals, warnings);
    }

    private async Task<IEnumerable<TripRefusal>> CheckAddedAsync(DateOnly day, IReadOnlyList<ShipmentOnTrip> added, Trip? current, CancellationToken cancellationToken)
    {
        if (added.Count == 0)
        {
            return [];
        }

        var onTrips = await trips.ListAsync(new TripsOfShipmentsSpec([.. added.Select(item => item.Shipment.Id)]), cancellationToken);
        var refusals = new List<TripRefusal>();
        foreach (var item in added)
        {
            var number = item.Shipment.Number.Value;
            if (item.Shipment is not { Status: ShipmentStatus.Released, CarriedBy.IsOwnFleet: true })
            {
                refusals.Add(TripRefusal.ShipmentNotReleased(number, item.Shipment.CarriedBy.IsOwnFleet ? $"it is {item.Shipment.Status}, not Released" : "it is carried by a Carrier, not the Own fleet"));
            }

            if (onTrips.FirstOrDefault(trip => trip.HasShipment(item.Shipment.Id) && trip.Number != current?.Number) is { } other)
            {
                refusals.Add(TripRefusal.ShipmentOnAnotherTrip(number, other.Number));
            }

            if (item.CollectionDay != day)
            {
                refusals.Add(TripRefusal.CollectionDayMismatch(number, item.CollectionDay, day));
            }
        }

        return refusals;
    }

    // A Driver without a Shift that day is DriverNotAvailable already, and a Trip without a Driver has no Shift to start in.
    private async Task<IReadOnlyList<TripRefusal>> CheckShiftAsync(DateOnly day, DriverId? driver, TimeOnly? start, CancellationToken cancellationToken)
    {
        if (driver is not { } driverId || start is not { } time)
        {
            return [];
        }

        var planningDay = await days.FirstOrDefaultAsync(new PlanningDayByDateSpec(day), cancellationToken);
        return planningDay?.Shifts.FirstOrDefault(shift => shift.DriverId == driverId) is { } shift && (time < shift.Start || time >= shift.End)
            ? [TripRefusal.OutsideShift(driverId.Value, time, day, shift.Start, shift.End)]
            : [];
    }
}
