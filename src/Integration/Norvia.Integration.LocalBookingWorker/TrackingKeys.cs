namespace Norvia.Integration.LocalBookingWorker;

/// <summary>
/// The keys of the tracking contract (coding guidelines §7) under the names the booking Logic App's tracked properties
/// give them, so the worker's traces join a Message trail as the workflow's do (ADR 0030).
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

    /// <summary>The <see cref="Flow"/> of a Carrier booking, from the release to the report to the TMS.</summary>
    public const string CarrierBooking = "carrier-booking";

    public const string Succeeded = "Succeeded";
    public const string Retrying = "Retrying";
    public const string Failed = "Failed";

    /// <summary>An outcome and, when there is one, its failure code; a key that doesn't apply is left out, never sent empty.</summary>
    public static Dictionary<string, object?> OutcomeOf(string outcome, string? failureCode = null) =>
        string.IsNullOrEmpty(failureCode)
            ? new() { [Outcome] = outcome }
            : new() { [Outcome] = outcome, [FailureCode] = failureCode };
}
