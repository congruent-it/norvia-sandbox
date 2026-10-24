namespace Norvia.Integration.LocalSupport;

/// <summary>
/// The keys of the tracking contract (coding guidelines §7) under the names API Management's traces and the Logic Apps'
/// tracked properties give them, so the stand-ins' traces join a Message trail as theirs do (ADR 0023, ADR 0030, ADR 0031).
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
    public const string SourceMessageId = "sourceMessageId";
    public const string MessageId = "messageId";
    public const string Outcome = "outcome";
    public const string FailureCode = "failureCode";
    public const string AgentId = "agentId";
    public const string ToolName = "toolName";

    /// <summary>The <see cref="Flow"/> of a booking, from the edge to the TMS's answer.</summary>
    public const string TransportOrderIntake = "transport-order-intake";

    /// <summary>The <see cref="Flow"/> of a Carrier's answer, from the edge to the TMS's answer.</summary>
    public const string CarrierAnswer = "carrier-answer";

    /// <summary>The <see cref="Flow"/> of a Carrier booking, from the release to the report to the TMS.</summary>
    public const string CarrierBooking = "carrier-booking";

    /// <summary>The <see cref="Flow"/> of a Shipper notification, from the request to the report to the TMS.</summary>
    public const string ShipperNotification = "shipper-notification";

    /// <summary>The <see cref="Flow"/> of a Carrier cancellation or withdrawal, from the event to the report to the TMS.</summary>
    public const string CarrierCancellation = "carrier-cancellation";

    /// <summary>The <see cref="Flow"/> of a Billable line, from TransportOrderBillable to the ERP billing API's answer.</summary>
    public const string Billing = "billing";

    /// <summary>The <see cref="Flow"/> of a Carrier cost, from CarrierCostIncurred to the ERP billing API's answer.</summary>
    public const string CarrierCost = "carrier-costs";

    /// <summary>The <see cref="Flow"/> of a Shipper invoice, from ShipperInvoiceIssued to the report to the ERP.</summary>
    public const string Invoicing = "invoicing";

    /// <summary>The <see cref="Flow"/> of a Carrier invoice, from the edge to the ERP billing API's answer.</summary>
    public const string CarrierInvoice = "carrier-invoice";

    /// <summary>The <see cref="Flow"/> of a Carrier invoice's outcome, from CarrierInvoiceApproved to the report to the ERP.</summary>
    public const string CarrierInvoices = "carrier-invoices";

    /// <summary>The <see cref="Flow"/> of a Trip version sent to the Driver app, from the dispatch to the report to the TMS.</summary>
    public const string TripDispatch = "trip-dispatch";

    /// <summary>The <see cref="Flow"/> of a Driver's action on a Trip (P12), from the Driver app's report to the TMS's answer.</summary>
    public const string TripExecution = "trip-execution";

    /// <summary>The <see cref="Flow"/> of a Carrier's tracking message, from the edge to the TMS.</summary>
    public const string CarrierTracking = "carrier-tracking";

    /// <summary>The <see cref="Flow"/> of a Carrier's Proof of delivery, from the edge to the TMS.</summary>
    public const string CarrierProofOfDelivery = "carrier-proof-of-delivery";

    /// <summary>The <see cref="Flow"/> of a Shipper's tracking query, from the edge to the TMS's answer.</summary>
    public const string ShipperTracking = "shipper-tracking";

    /// <summary>The <see cref="Outcome"/>s the stand-ins record.</summary>
    public const string Succeeded = "Succeeded";
    public const string Refused = "Refused";
    public const string Retrying = "Retrying";
    public const string DeadLettered = "DeadLettered";
    public const string Failed = "Failed";

    /// <summary>
    /// An outcome and, when there is one, its failure code; a key that doesn't apply is left out, never sent empty. The
    /// <paramref name="messageId"/> of the Service Bus message is recorded only on a <see cref="DeadLettered"/> outcome,
    /// so the console can join the hop to the message it dead-lettered.
    /// </summary>
    public static Dictionary<string, object?> OutcomeOf(string outcome, string? failureCode = null, string? messageId = null)
    {
        var keys = new Dictionary<string, object?> { [Outcome] = outcome };
        if (!string.IsNullOrEmpty(failureCode))
        {
            keys[FailureCode] = failureCode;
        }

        if (outcome == DeadLettered && !string.IsNullOrEmpty(messageId))
        {
            keys[MessageId] = messageId;
        }

        return keys;
    }
}
