namespace Norvia.DriverApp.Application;

/// <summary>The keys of the tracking contract (coding guidelines §7), under the same names as everywhere else, so the Integration console finds every trace of a Message trail.</summary>
public static class TrackingKeys
{
    public const string CorrelationId = "correlationId";
    public const string Flow = "flow";
    public const string TripNumber = "tripNumber";
    public const string ShipmentNumber = "shipmentNumber";
    public const string PartyId = "partyId";
    public const string SourceMessageId = "sourceMessageId";
    public const string Outcome = "outcome";
    public const string FailureCode = "failureCode";

    /// <summary>The name of the runtime fault that made a hop fail, set only on a hop a fault caused.</summary>
    public const string Fault = "fault";

    public const string Succeeded = "Succeeded";
    public const string Refused = "Refused";
    public const string Failed = "Failed";
}
