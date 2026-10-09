using Ardalis.SharedKernel;
using Norvia.Tms.Domain.Fleet;

namespace Norvia.Tms.Application.Fleet.Availability;

/// <summary>
/// Whether a Vehicle and Driver can take a Trip (P10's availability rules, which P11 applies to every change to a Trip and
/// again at Dispatch). It answers from the day's availability and the Driving-hour budget, and keeps no state of its own.
/// </summary>
public sealed class TripAvailabilityRules(
    IReadRepository<PlanningDay> days,
    IVehicleDirectory vehicles,
    IDriverDirectory drivers,
    IPlannedDrivingHours plannedDriving,
    DrivingHourLimits limits,
    PlanningHorizonRules horizon,
    TimeProvider time)
{
    /// <summary>Every rule the Trip breaks, in the order of the rules; empty when the Vehicle and Driver can take it.</summary>
    public async Task<IReadOnlyList<TripAvailabilityFailure>> CheckAsync(TripProposal trip, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(trip);
        var failures = new List<TripAvailabilityFailure>();

        var inHorizon = horizon.DaysFrom(horizon.Today(time.GetUtcNow())).Contains(trip.Day);
        var day = inHorizon ? await days.FirstOrDefaultAsync(new PlanningDayByDateSpec(trip.Day), cancellationToken) : null;
        if (day is null)
        {
            failures.Add(TripAvailabilityFailure.DayNotOpen(trip.Day));
        }

        var vehicle = trip.Vehicle is { } vehicleId ? vehicles.ListActive().FirstOrDefault(active => active.Id == vehicleId) : null;
        var driver = trip.Driver is { } driverId ? drivers.ListActive().FirstOrDefault(active => active.Id == driverId) : null;

        CheckVehicle(trip, day, vehicle, failures);
        CheckDriver(trip, day, driver, failures);

        if (vehicle is not null && driver is not null && !driver.CanDrive(vehicle.Type))
        {
            failures.Add(TripAvailabilityFailure.LicenceNotHeld(driver.Id.Value, vehicle.Id.Value, LicenceRules.Needed(vehicle.Type)));
        }

        if (driver is not null && trip.DrivingDuration is { } duration && !await FitsBudgetAsync(driver, trip.Day, duration, cancellationToken))
        {
            failures.Add(TripAvailabilityFailure.DrivingHoursExceeded(driver.Id.Value, duration));
        }

        if (vehicle is not null)
        {
            if (trip.Load.WeightKg > vehicle.PayloadKg)
            {
                failures.Add(TripAvailabilityFailure.PayloadExceeded(vehicle.Id.Value, trip.Load.WeightKg, vehicle.PayloadKg));
            }

            if (trip.Load.PalletPlaces > vehicle.PalletPlaces)
            {
                failures.Add(TripAvailabilityFailure.PalletPlacesExceeded(vehicle.Id.Value, trip.Load.PalletPlaces, vehicle.PalletPlaces));
            }
        }

        return Ordered(failures);
    }

    private static void CheckVehicle(TripProposal trip, PlanningDay? day, Vehicle? vehicle, List<TripAvailabilityFailure> failures)
    {
        if (trip.Vehicle is not { } vehicleId)
        {
            return;
        }

        // An inactive or unknown Vehicle isn't in the active list; a day we can't see says nothing about it.
        var why = vehicle is null ? "it is not an active Vehicle"
            : day is not null && !day.AvailableVehicles.Contains(vehicleId) ? "it is off the road"
            : trip.VehicleHasOtherTrip ? "it has another Trip that day"
            : null;
        if (why is not null)
        {
            failures.Add(TripAvailabilityFailure.VehicleNotAvailable(vehicleId.Value, trip.Day, why));
        }
    }

    private static void CheckDriver(TripProposal trip, PlanningDay? day, Driver? driver, List<TripAvailabilityFailure> failures)
    {
        if (trip.Driver is not { } driverId)
        {
            return;
        }

        // An Absence takes the Shift away, so a Driver with a Shift has no Absence.
        var why = driver is null ? "they are not an active Driver"
            : day is not null && day.Shifts.All(shift => shift.DriverId != driverId) ? "they have no Shift that day"
            : trip.DriverHasOtherTrip ? "they have another Trip that day"
            : null;
        if (why is not null)
        {
            failures.Add(TripAvailabilityFailure.DriverNotAvailable(driverId.Value, trip.Day, why));
        }
    }

    private async Task<bool> FitsBudgetAsync(Driver driver, DateOnly tripDay, TimeSpan duration, CancellationToken cancellationToken)
    {
        var (first, last) = DrivingHourBudget.Window(tripDay);
        var planned = await plannedDriving.ListAsync(first, last, cancellationToken);
        return DrivingHourBudget.Fits(planned.Where(driving => driving.DriverId == driver.Id), new PlannedDriving(driver.Id, tripDay, duration), limits);
    }

    private static List<TripAvailabilityFailure> Ordered(List<TripAvailabilityFailure> failures)
    {
        failures.Sort((a, b) => a.Code.CompareTo(b.Code));
        return failures;
    }
}
