namespace Norvia.Integration.LocalTrackingWorker;

/// <summary>
/// The keys of the tracking contract (coding guidelines §7) under the names the Carrier-tracking Logic App's tracked
/// properties give them, so the worker's traces join a Message trail as the workflow's do (ADR 0031).
/// </summary>
internal static class TrackingKeys
{
    public const string CorrelationId = "correlationId";
    public const string Flow = "flow";
    public const string PartyId = "partyId";
    public const string ShipmentNumber = "shipmentNumber";
    public const string CarrierBookingNumber = "carrierBookingNumber";
    public const string SourceMessageId = "sourceMessageId";
    public const string Outcome = "outcome";
    public const string FailureCode = "failureCode";

    /// <summary>The <see cref="Flow"/> of a Carrier's tracking message, from the edge to the TMS.</summary>
    public const string CarrierTracking = "carrier-tracking";

    /// <summary>The dead-letter reason, and failure code, of a message the map can't read.</summary>
    public const string UnmappableTrackingMessage = "UnmappableTrackingMessage";
}
