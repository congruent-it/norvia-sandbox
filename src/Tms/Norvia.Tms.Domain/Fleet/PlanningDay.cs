using Ardalis.SharedKernel;

namespace Norvia.Tms.Domain.Fleet;

/// <summary>
/// One day of the Planning horizon (P10): the Shifts the Drivers work and the Vehicles available. The horizon job opens
/// it from the Rosters, and closes it when it is before today, after which it is read-only.
/// </summary>
public sealed class PlanningDay : EntityBase<PlanningDayId>, IAggregateRoot
{
    private PlanningDay(DateOnly date, IReadOnlyList<Shift> shifts, IReadOnlyList<VehicleId> availableVehicles)
    {
        Id = new PlanningDayId(date);
        Date = date;
        Shifts = shifts;
        AvailableVehicles = availableVehicles;
        Status = PlanningDayStatus.Open;
    }

    public DateOnly Date { get; private set; }

    public PlanningDayStatus Status { get; private set; }

    /// <summary>The Drivers who work this day, with when; a Driver without a Shift doesn't work it.</summary>
    public IReadOnlyList<Shift> Shifts { get; private set; }

    /// <summary>The active Vehicles the Own fleet can use this day.</summary>
    public IReadOnlyList<VehicleId> AvailableVehicles { get; private set; }

    /// <summary>
    /// Opens a day: each Driver whose Roster has work that weekday gets a Shift, and every Vehicle given is available.
    /// </summary>
    public static PlanningDay Open(DateOnly date, IEnumerable<Roster> rosters, IEnumerable<VehicleId> vehicles)
    {
        ArgumentNullException.ThrowIfNull(rosters);
        ArgumentNullException.ThrowIfNull(vehicles);
        IReadOnlyList<Shift> shifts =
        [
            .. rosters.Select(roster => (roster.DriverId, Times: roster.ShiftOn(date)))
                .Where(entry => entry.Times is not null)
                .Select(entry => new Shift(entry.DriverId, entry.Times!.Start, entry.Times.End, EditedByHand: false)),
        ];
        return new PlanningDay(date, shifts, [.. vehicles]);
    }

    /// <summary>Closes the day. Closing a closed day changes nothing.</summary>
    public void Close() => Status = PlanningDayStatus.Closed;

    /// <summary>Closes the day when it is before today, so it is read-only even before the horizon job has got to it.</summary>
    public void CloseIfBefore(DateOnly today)
    {
        if (Date < today)
        {
            Close();
        }
    }

    /// <summary>
    /// Sets one Driver's Shift on this day by hand, replacing the one from their Roster, or adding one on a day off. A
    /// Roster change won't overwrite it. Refuses on a closed day; returns null when the Shift is set.
    /// </summary>
    public PlanningRefusal? EditShift(DriverId driverId, TimeOnly start, TimeOnly end)
    {
        ArgumentNullException.ThrowIfNull(driverId);
        if (Status == PlanningDayStatus.Closed)
        {
            return PlanningRefusal.DayClosed(Date);
        }

        if (start >= end)
        {
            return PlanningRefusal.ShiftInvalid();
        }

        var edited = new Shift(driverId, start, end, EditedByHand: true);
        Shifts = [.. Shifts.Where(shift => shift.DriverId != driverId), edited];
        return null;
    }
}
