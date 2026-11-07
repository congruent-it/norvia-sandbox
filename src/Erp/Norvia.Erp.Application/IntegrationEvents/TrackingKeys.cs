namespace Norvia.Erp.Application.IntegrationEvents;

/// <summary>
/// The keys of the tracking contract (coding guidelines §7), under the same names in logging scopes and on Service Bus
/// messages, so the Integration console finds every trace of a Message trail.
/// </summary>
public static class TrackingKeys
{
    public const string CorrelationId = "correlationId";
    public const string Flow = "flow";
    public const string PartyId = "partyId";
    public const string TransportOrderNumber = "transportOrderNumber";
    public const string ShipmentNumber = "shipmentNumber";
    public const string CarrierBookingNumber = "carrierBookingNumber";
    public const string TripNumber = "tripNumber";
    public const string DocumentNumber = "documentNumber";
    public const string Outcome = "outcome";

    /// <summary>The identity of the Agent that made the call (the token's <c>azp</c>), on a call to an agent view.</summary>
    public const string AgentId = "agentId";

    /// <summary>The ID of the Agent run whose Suggestion a person acted on, on the trace of what they did (ADR 0040), next to <see cref="AgentId"/>.</summary>
    public const string AgentRunId = "agentRunId";

    /// <summary>The Agent tool the call was made for, on a call to an agent view.</summary>
    public const string ToolName = "toolName";

    /// <summary>The sender's own ID for the message, such as a Carrier's document ID.</summary>
    public const string SourceMessageId = "sourceMessageId";

    /// <summary>The stable failure code, when there is one, such as a rating failure's <c>NoMatchingLane</c>.</summary>
    public const string FailureCode = "failureCode";

    /// <summary>
    /// The <see cref="Outcome"/> of a step that gave its answer, whatever the answer: Rating failed is Rating's answer, with
    /// its <see cref="FailureCode"/>, not a failed flow, as the Transport Order stays Accepted.
    /// </summary>
    public const string Succeeded = "Succeeded";

    /// <summary>The <see cref="Outcome"/> of a call the application refused, such as one without the right role.</summary>
    public const string Refused = "Refused";

    /// <summary>The <see cref="Outcome"/> of a step that didn't give its answer and will be tried again.</summary>
    public const string Retrying = "Retrying";
}
