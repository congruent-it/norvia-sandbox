namespace Norvia.Tms.Api.Faults;

/// <summary>The names of the faults the TMS owns. The presenter panel switches them, and a hop one caused carries the name as its <c>fault</c> key.</summary>
public static class TmsFaultNames
{
    /// <summary>P12: while on, the TMS API answers every call with a 503, and keeps nothing. The Driver reports wait in their sessions and are processed in order afterwards.</summary>
    public const string Unavailable = "tms-unavailable";

    /// <summary>P5: while on, the Tracking event API answers 503. The Carrier-tracking messages wait on the queue and are retried.</summary>
    public const string TrackingEventApiFailing = "tms-tracking-event-api-failing";

    /// <summary>P8: while on, the Shipper tracking API answers 503, which API Management passes on, so the Shipper sees the error at once.</summary>
    public const string ShipperTrackingApiFailing = "tms-shipper-tracking-api-failing";
}
