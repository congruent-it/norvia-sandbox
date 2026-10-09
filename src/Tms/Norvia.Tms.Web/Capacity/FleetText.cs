using System.Globalization;
using Norvia.Tms.Application.Fleet.Planning;
using Norvia.Tms.Domain.Fleet;

namespace Norvia.Tms.Web.Capacity;

/// <summary>How the Capacity view words the Own fleet's Vehicles and Drivers.</summary>
internal static class FleetText
{
    public static string VehicleType(VehicleType type) => type switch
    {
        Domain.Fleet.VehicleType.Van => "Van",
        Domain.Fleet.VehicleType.RigidTruck => "Rigid truck",
        Domain.Fleet.VehicleType.TractorWithTrailer => "Tractor with trailer",
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Unknown Vehicle type."),
    };

    public static string Payload(int kg) => $"{kg:N0} kg";

    public static string Licences(IEnumerable<LicenceCategory> licences) => string.Join(", ", licences);

    public static string VehicleTypes(IEnumerable<VehicleType> types) => string.Join(", ", types.Select(VehicleType));

    /// <summary>Driving hours as "9 h" or "7.5 h".</summary>
    public static string Hours(TimeSpan hours) => $"{hours.TotalHours.ToString("0.##", CultureInfo.InvariantCulture)} h";

    /// <summary>A day as the Planner reads it, such as "Mon 12 Oct".</summary>
    public static string Day(DateOnly date) => date.ToString("ddd d MMM", CultureInfo.InvariantCulture);

    /// <summary>A Shift as "06:00-14:30", with a note when the Planner edited it by hand.</summary>
    public static string Shift(TimeOnly start, TimeOnly end, bool editedByHand) =>
        $"{start.ToString("HH:mm", CultureInfo.InvariantCulture)}-{end.ToString("HH:mm", CultureInfo.InvariantCulture)}{(editedByHand ? " (edited)" : string.Empty)}";

    public static string DriversOnShift(IEnumerable<ShiftView> shifts) =>
        string.Join("; ", shifts.Select(shift => $"{shift.DriverName} {Shift(shift.Start, shift.End, shift.EditedByHand)}"));

    /// <summary>A day's weight against its capacity, such as "8,500 / 10,000 kg".</summary>
    public static string WeightLoad(DayLoadView load) => $"{load.DemandKg:N0} / {load.CapacityKg:N0} kg";

    /// <summary>A day's pallet places against its capacity, such as "12 / 18".</summary>
    public static string PalletLoad(DayLoadView load) => $"{load.DemandPalletPlaces:N0} / {load.CapacityPalletPlaces:N0}";

    /// <summary>"Over capacity" for a day whose demand is more than its capacity, and nothing for one that fits.</summary>
    public static string OverCapacity(DayLoadView load) => load.Over ? "Over capacity" : string.Empty;

    /// <summary>A Roster as "Mon 06:00-14:00; Tue 06:00-14:00", or "No work" when the Driver has no Roster.</summary>
    public static string Roster(IEnumerable<RosterWeekdayView> weekdays)
    {
        var text = string.Join("; ", weekdays.Select(weekday => $"{Weekday(weekday.Weekday)} {Shift(weekday.Start, weekday.End, editedByHand: false)}"));
        return text.Length == 0 ? "No work" : text;
    }

    public static string Weekday(DayOfWeek weekday) => CultureInfo.InvariantCulture.DateTimeFormat.GetAbbreviatedDayName(weekday);

    /// <summary>A reason as the Planner reads it, such as "Leave" or "Maintenance"; the remark follows when there is one.</summary>
    public static string Reason<TReason>(TReason reason, string remark)
        where TReason : struct, Enum =>
        remark.Length == 0 ? reason.ToString() : $"{reason}: {remark}";
}
