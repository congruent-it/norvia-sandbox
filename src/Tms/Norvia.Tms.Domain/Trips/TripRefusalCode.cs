namespace Norvia.Tms.Domain.Trips;

/// <summary>
/// Why the TMS refused a Planner's action on a Trip, apart from the availability rules of P10, whose codes are
/// <see cref="Fleet.TripAvailabilityCode"/>. The names are stable codes: never rename one, only add.
/// </summary>
public enum TripRefusalCode
{
    /// <summary>There is no Trip with that number.</summary>
    TripNotFound = 1,

    /// <summary>The Trip is Dispatched, so it can't be changed by hand as a Draft can, nor discarded.</summary>
    TripNotDraft,
}
