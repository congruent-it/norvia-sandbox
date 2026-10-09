namespace Norvia.Tms.Domain.Fleet;

/// <summary>
/// Why a Vehicle and Driver can't take a Trip (P10's availability rules, which P11 applies). The names are stable codes
/// that callers and monitoring rely on: never rename one, only add.
/// </summary>
public enum TripAvailabilityCode
{
    /// <summary>The Vehicle is inactive or Off the road that day, or has another Trip that day.</summary>
    VehicleNotAvailable = 1,

    /// <summary>The Driver is inactive, has no Shift that day or an Absence, or has another Trip that day.</summary>
    DriverNotAvailable,

    /// <summary>The Driver's licence doesn't cover the Vehicle type.</summary>
    LicenceNotHeld,

    /// <summary>The Trip's planned driving duration doesn't fit in the Driver's Driving-hour budget.</summary>
    DrivingHoursExceeded,

    /// <summary>The Trip's total weight is more than the Vehicle's payload.</summary>
    PayloadExceeded,

    /// <summary>The Trip's pallets are more than the Vehicle's pallet places.</summary>
    PalletPlacesExceeded,

    /// <summary>The Trip's day is not in the Planning horizon.</summary>
    DayNotOpen,
}
