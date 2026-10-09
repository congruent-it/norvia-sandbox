using static System.FormattableString;

namespace Norvia.Tms.Domain.Fleet;

/// <summary>A rule a Vehicle and Driver break for a Trip (P10): a stable code and a readable message.</summary>
public sealed record TripAvailabilityFailure(TripAvailabilityCode Code, string Message)
{
    public static TripAvailabilityFailure VehicleNotAvailable(string vehicleId, DateOnly day, string why) =>
        new(TripAvailabilityCode.VehicleNotAvailable, Invariant($"Vehicle {vehicleId} is not available on {day:yyyy-MM-dd}: {why}."));

    public static TripAvailabilityFailure DriverNotAvailable(string driverId, DateOnly day, string why) =>
        new(TripAvailabilityCode.DriverNotAvailable, Invariant($"Driver {driverId} is not available on {day:yyyy-MM-dd}: {why}."));

    public static TripAvailabilityFailure LicenceNotHeld(string driverId, string vehicleId, LicenceCategory needed) =>
        new(TripAvailabilityCode.LicenceNotHeld, Invariant($"Driver {driverId} does not hold a licence for Vehicle {vehicleId}, which needs category {needed}."));

    public static TripAvailabilityFailure DrivingHoursExceeded(string driverId, TimeSpan duration) =>
        new(TripAvailabilityCode.DrivingHoursExceeded, Invariant($"A planned driving duration of {duration.TotalHours:0.##} h does not fit in Driver {driverId}'s Driving-hour budget."));

    public static TripAvailabilityFailure PayloadExceeded(string vehicleId, decimal weightKg, int payloadKg) =>
        new(TripAvailabilityCode.PayloadExceeded, Invariant($"The Trip weighs {weightKg:0.##} kg, more than Vehicle {vehicleId} can carry ({payloadKg} kg)."));

    public static TripAvailabilityFailure PalletPlacesExceeded(string vehicleId, int pallets, int palletPlaces) =>
        new(TripAvailabilityCode.PalletPlacesExceeded, Invariant($"The Trip has {pallets} pallets, more than Vehicle {vehicleId} has places for ({palletPlaces})."));

    public static TripAvailabilityFailure DayNotOpen(DateOnly day) =>
        new(TripAvailabilityCode.DayNotOpen, Invariant($"{day:yyyy-MM-dd} is not in the Planning horizon."));
}
