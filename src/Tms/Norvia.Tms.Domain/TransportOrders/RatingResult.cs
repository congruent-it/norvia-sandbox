namespace Norvia.Tms.Domain.TransportOrders;

/// <summary>
/// Where a Transport Order stands commercially (P2 Rating). It is separate from its status: a Transport Order can be
/// planned whatever its rating result.
/// </summary>
public enum RatingResult
{
    /// <summary>Accepted, and Rating hasn't run yet.</summary>
    NotYetRated = 1,

    /// <summary>A Freight Charge has been calculated and is frozen. Final.</summary>
    Rated,

    /// <summary>A Fulfilment Transport Order, billed through the Fulfilment Contract rather than per transport. Final.</summary>
    NotRatedSeparately,

    /// <summary>The Rate Card couldn't price the Transport Order; it carries the failure. A re-rate can still rate it.</summary>
    RatingFailed,
}
