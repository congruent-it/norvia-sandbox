namespace Norvia.PartnerPortal.Application.ShipperTracking;

/// <summary>How the Norvia Partner API answered a tracking query.</summary>
public enum TrackingOutcome
{
    /// <summary>It answered with what was asked for.</summary>
    Found,

    /// <summary>There is no such Transport Order or file for this Shipper, or no such Shipper Persona.</summary>
    NotFound,

    /// <summary>Tracking is temporarily unavailable, or the answer never came or couldn't be read. Asking again later may work.</summary>
    Unavailable,

    /// <summary>The edge refused the query: no role, a rate limit or a bad query. Asking again as it was won't help.</summary>
    Refused,
}
