namespace Norvia.Tms.Application.Trips;

/// <summary>
/// The P10 flags on a Trip (P11): <b>Needs a Driver</b> and <b>Needs a Vehicle</b>. They are computed from the Trip, never stored, so
/// they clear as soon as the rule passes again.
/// </summary>
public readonly record struct TripNeeds(bool Driver, bool Vehicle)
{
    public static readonly TripNeeds None = new(false, false);

    public bool Any => Driver || Vehicle;
}
