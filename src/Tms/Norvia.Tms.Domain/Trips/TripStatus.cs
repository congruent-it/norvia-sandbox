namespace Norvia.Tms.Domain.Trips;

public enum TripStatus
{
    /// <summary>Being built. The Planner can change anything or discard it, and it still holds its Vehicle and Driver.</summary>
    Draft = 1,

    /// <summary>Sent to its Driver. It is never deleted.</summary>
    Dispatched,
}
