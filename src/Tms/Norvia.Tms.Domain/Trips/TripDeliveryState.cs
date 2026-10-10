namespace Norvia.Tms.Domain.Trips;

/// <summary>Whether the Driver app has one version of a Trip (P11).</summary>
public enum TripDeliveryState
{
    /// <summary>Published, not yet reported by the Trip dispatch Logic App.</summary>
    Sending = 1,

    /// <summary>The Driver app accepted this version.</summary>
    Delivered,

    /// <summary>The Logic App gave up after its retries. The Planner can send the version again.</summary>
    NotDelivered,
}
