namespace Norvia.Tms.Domain.ShipperTracking;

/// <summary>
/// Why the TMS refused a Consignee's tracking query (ADR 0052). The names are stable codes that callers and monitoring rely on: never rename one, only add.
/// </summary>
public enum ConsigneeTrackingRefusalCode
{
    /// <summary>The code is malformed, unknown, expired or was replaced: one answer for all four, so the API doesn't say whether a code ever existed.</summary>
    TrackingCodeNotFound = 1,
}
