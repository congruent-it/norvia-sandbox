namespace Norvia.Integration.LocalPartnerApi;

/// <summary>
/// The keys of the tracking contract (coding guidelines §7) under the names API Management's traces and the intake
/// Logic App's tracked properties give them, so the stand-in's traces join a Message trail as theirs do (ADR 0023).
/// </summary>
internal static class TrackingKeys
{
    public const string CorrelationId = "correlationId";
    public const string Flow = "flow";
    public const string PartyId = "partyId";
    public const string SourceMessageId = "sourceMessageId";
    public const string TransportOrderNumber = "transportOrderNumber";
    public const string ShipmentNumber = "shipmentNumber";
    public const string CarrierBookingNumber = "carrierBookingNumber";
    public const string Outcome = "outcome";
    public const string FailureCode = "failureCode";

    /// <summary>The <see cref="Flow"/> of a booking, from the edge to the TMS's answer.</summary>
    public const string TransportOrderIntake = "transport-order-intake";

    /// <summary>The <see cref="Flow"/> of a Carrier's answer, from the edge to the TMS's answer.</summary>
    public const string CarrierAnswer = "carrier-answer";

    /// <summary>The <see cref="Flow"/> of a Carrier's tracking message, from the edge to the tracking queue.</summary>
    public const string CarrierTracking = "carrier-tracking";

    /// <summary>The <see cref="Outcome"/>s the stand-in records.</summary>
    public const string Succeeded = "Succeeded";
    public const string Refused = "Refused";
    public const string Failed = "Failed";

    /// <summary>An outcome and, when there is one, its failure code; a key that doesn't apply is left out, never sent empty.</summary>
    public static Dictionary<string, object?> OutcomeOf(string outcome, string? failureCode = null) =>
        string.IsNullOrEmpty(failureCode)
            ? new() { [Outcome] = outcome }
            : new() { [Outcome] = outcome, [FailureCode] = failureCode };
}
