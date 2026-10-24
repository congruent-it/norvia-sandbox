using Norvia.ServiceDefaults.Faults;

namespace Norvia.Tms.Api.Faults;

public static class TmsFaultDefinitions
{
    public const string Owner = "tms";

    public static readonly FaultDefinition Unavailable = new(
        TmsFaultNames.Unavailable,
        Owner,
        "The TMS API answers every call with a 503: the Driver reports wait in their sessions and are processed in order once it is back, so nothing is lost.");

    public static readonly FaultDefinition TrackingEventApiFailing = new(
        TmsFaultNames.TrackingEventApiFailing,
        Owner,
        "The TMS Tracking event API answers 503: the Carrier-tracking messages wait on the queue and are retried, so nothing is lost.");

    public static readonly FaultDefinition ShipperTrackingApiFailing = new(
        TmsFaultNames.ShipperTrackingApiFailing,
        Owner,
        "The TMS Shipper tracking API answers 503: the Shipper sees the error at once.");

    public static readonly FaultDefinition[] All = [Unavailable, TrackingEventApiFailing, ShipperTrackingApiFailing];
}
