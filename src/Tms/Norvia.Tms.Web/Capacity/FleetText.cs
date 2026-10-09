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

    /// <summary>A day as the Planner reads it, such as "Mon 12 Oct".</summary>
    public static string Day(DateOnly date) => date.ToString("ddd d MMM", CultureInfo.InvariantCulture);

    /// <summary>A Shift as "06:00-14:30", with a note when the Planner edited it by hand.</summary>
    public static string Shift(TimeOnly start, TimeOnly end, bool editedByHand) =>
        $"{start.ToString("HH:mm", CultureInfo.InvariantCulture)}-{end.ToString("HH:mm", CultureInfo.InvariantCulture)}{(editedByHand ? " (edited)" : string.Empty)}";

    public static string DriversOnShift(IEnumerable<ShiftView> shifts) =>
        string.Join("; ", shifts.Select(shift => $"{shift.DriverName} {Shift(shift.Start, shift.End, shift.EditedByHand)}"));
}
