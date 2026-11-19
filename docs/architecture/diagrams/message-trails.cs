#:project ../../../src/Docs/Norvia.Docs.Diagrams/Norvia.Docs.Diagrams.csproj

// The Message trails of the process docs, as sequence diagrams: the Hops of each flow, as the Integration console shows them, in time order.
//
//     dotnet run docs/architecture/diagrams/message-trails.cs -- [name ...]    # e.g. p9-1; writes message-trail-<name>.excalidraw and .png here; no names draws all
//
// Sequence style (see README.md). Each spec belongs to the process doc named in its footnote, which shows it under Message trail. The PNGs need rsvg-convert.

using Norvia.Docs.Diagrams;

static LegendItem L(string label, string key) => Palette.Legend(label, key);
static SequenceParticipant P(string id, string title, string pal = "gray", string sub = "") => new(id, title, pal, sub);
LegendItem[] Legend = [L("Partner portal", "gray"), L("Integration layer", "violet"), L("TMS", "blue"), L("ERP", "green"), L("Driver app", "teal")];
const string LegendArrows = "Solid arrows are calls, dashed arrows are replies and events. Each Hop is named application: operation.";

var specs = new Dictionary<string, SequenceSpec>();

specs["e1-1"] = new SequenceSpec
{
    Title = "E1 Invoicing · A Billable line",
    Subtitle = "Flow billing. The Hops as the Integration console shows them, in time order.",
    Footnote = LegendArrows + " Source of truth: docs/processes/erp/e1-invoicing.md, its Message trail.",
    Legend = Legend,
    Groups = [new SequenceGroup("Flow billing", ["sub", "logic", "erp"])],
    Participants = [
        P("sub", "subscription billing", "violet", "transport-order-events (topic)"),
        P("logic", "billing", "violet", "logic-billing"),
        P("erp", "POST /v1/billable-lines", "green", "erp-api"),
    ],
    Steps = [
        new Message("sub", "logic", "A TransportOrderBillable message is available", Dashed: true),
        new SelfStep("logic", "Track_the_billable_line (Transport Order number, Shipment number, Shipper as partyId)"),
        new Message("logic", "erp", "Record_the_billable_line (the message body as it is, X-Correlation-ID)"),
        new SelfStep("erp", "Billable line Waiting. Idempotent on the Transport Order number. Nothing is published"),
        new Message("erp", "logic", "201, 200 (repeat), or 409, 422, 400 with a stable code", Dashed: true),
        new Message("logic", "sub", "Settle_the_message: complete, or dead-letter with the ERP's code"),
    ],
};


specs["e1-2"] = new SequenceSpec
{
    Title = "E1 Invoicing · Invoice now",
    Subtitle = "Flow invoicing. The Hops as the Integration console shows them, in time order.",
    Footnote = LegendArrows + " Source of truth: docs/processes/erp/e1-invoicing.md, its Message trail.",
    Legend = Legend,
    Groups = [new SequenceGroup("Flow invoicing", ["erp", "pub", "logic", "portal", "report"])],
    Participants = [
        P("erp", "Invoice now", "green", "erp-web"),
        P("pub", "publish ShipperInvoiceIssued", "green", "erp-api"),
        P("logic", "shipper-invoice", "violet", "logic-shipper-invoice"),
        P("portal", "POST /shipper/v1/shipper-id/invoices", "gray", "partner-portal-web"),
        P("report", "POST /v1/shipper-invoices/number/report-sent", "green", "erp-api"),
    ],
    Steps = [
        new SelfStep("erp", "IssueShipperInvoiceCommand: Waiting lines Invoiced, invoice numbered, outbox message, one transaction"),
        new Message("erp", "pub", "outbox, within a second", Dashed: true),
        new Message("pub", "logic", "invoicing-events, subscription shipper-invoice", Dashed: true),
        new SelfStep("logic", "Track_the_invoice, Map_the_invoice (MapShipperDocument), Complete_the_message"),
        new SelfStep("logic", "Get_the_Shipper_endpoint_credential (Key Vault)"),
        new Message("logic", "portal", "Send_to_the_Shipper (the invoice in the Shipper's format)"),
        new Message("portal", "logic", "200 acknowledged", Dashed: true),
        new Message("logic", "report", "Report_the_outcome_to_the_ERP: Report_the_invoice_sent"),
        new Message("report", "logic", "200 (or 200 ignored for a repeat)", Dashed: true),
    ],
};


specs["e1-3"] = new SequenceSpec
{
    Title = "E1 Invoicing · An invoice the Shipper never got",
    Subtitle = "Flow invoicing. The Hops as the Integration console shows them, in time order.",
    Footnote = LegendArrows + " Source of truth: docs/processes/erp/e1-invoicing.md, its Message trail.",
    Legend = Legend,
    Groups = [new SequenceGroup("Flow invoicing", ["logic", "portal", "report", "pub", "again"])],
    Participants = [
        P("logic", "shipper-invoice", "violet", "logic-shipper-invoice"),
        P("portal", "POST /shipper/v1/shipper-id/invoices", "gray", "partner-portal-web"),
        P("report", "POST /v1/shipper-invoices/number/report-not-delivered", "green", "erp-api"),
        P("pub", "publish ShipperInvoiceNotDelivered", "green", "erp-api"),
        P("again", "Send again", "green", "erp-web"),
    ],
    Steps = [
        new Message("logic", "portal", "Send_to_the_Shipper (retried three times on a timeout, 408, 429 or 5xx)"),
        new Message("portal", "logic", "no answer, or a client error", Dashed: true),
        new Message("logic", "report", "Report_the_outcome_to_the_ERP: Report_the_invoice_not_delivered (status code, cause up to 500 characters)"),
        new SelfStep("report", "Invoice Not delivered, with the outbox message, one transaction"),
        new Message("report", "pub", "outbox, within a second", Dashed: true),
        new SelfStep("again", "SendShipperInvoiceAgainCommand: back to Issued, ShipperInvoiceIssued published again, same correlation ID"),
    ],
};


specs["e2-1"] = new SequenceSpec
{
    Title = "E2 Carrier invoice matching · A Carrier cost",
    Subtitle = "Flow carrier-costs. The Hops as the Integration console shows them, in time order.",
    Footnote = LegendArrows + " Source of truth: docs/processes/erp/e2-carrier-invoice-matching.md, its Message trail.",
    Legend = Legend,
    Groups = [new SequenceGroup("Flow carrier-costs", ["sub", "logic", "erp"])],
    Participants = [
        P("sub", "subscription carrier-cost", "violet", "shipment-events (topic)"),
        P("logic", "carrier-cost", "violet", "logic-carrier-cost"),
        P("erp", "POST /v1/carrier-costs", "green", "erp-api"),
    ],
    Steps = [
        new Message("sub", "logic", "A CarrierCostIncurred message is available", Dashed: true),
        new SelfStep("logic", "Track_the_carrier_cost, Check_the_carrier_cost"),
        new Message("logic", "erp", "Record_the_carrier_cost (the message body as it is, X-Correlation-ID)"),
        new SelfStep("erp", "Carrier cost Open. Undecided Unknown booking lines of that Carrier with that reference are matched again, in the same save"),
        new Message("erp", "logic", "201, 200 (repeat), or 409, 422, 400 with a stable code", Dashed: true),
        new Message("logic", "sub", "Settle_the_message: complete, or dead-letter with the ERP's code"),
    ],
};


specs["e2-2"] = new SequenceSpec
{
    Title = "E2 Carrier invoice matching · A Carrier invoice",
    Subtitle = "Flow carrier-invoice. The Hops as the Integration console shows them, in time order.",
    Footnote = LegendArrows + " Source of truth: docs/processes/erp/e2-carrier-invoice-matching.md, its Message trail.",
    Legend = Legend,
    Groups = [new SequenceGroup("Flow carrier-invoice", ["portal", "apim", "logic", "erp", "pub"])],
    Participants = [
        P("portal", "Submit Carrier invoice", "gray", "partner-portal-web"),
        P("apim", "Submit Carrier invoice", "violet", "apim-norvia"),
        P("logic", "carrier-invoice", "violet", "logic-carrier-invoice"),
        P("erp", "POST /v1/carrier-invoices", "green", "erp-api"),
        P("pub", "publish CarrierInvoiceApproved", "green", "erp-api"),
    ],
    Steps = [
        new Message("portal", "apim", "POST /partner/v1/carrier-invoices (Persona token and subscription key)"),
        new SelfStep("apim", "token, role CarrierInvoices.Submit, Carrier ID from azp, correlation ID"),
        new Message("apim", "logic", "Canonical call, X-Carrier-ID and X-Correlation-ID"),
        new SelfStep("logic", "Refuse any caller but API Management, Track_the_invoice, Map_the_invoice (MapCarrierInvoice)"),
        new Message("logic", "erp", "Receive_at_the_ERP (Canonical Model submission)"),
        new SelfStep("erp", "Every line matched against the Carrier's Carrier costs, matched Carrier costs Invoiced, one transaction"),
        new Message("erp", "logic", "201 or 200 (repeat), each line's result, or a refusal code", Dashed: true),
        new SelfStep("logic", "Relay_the_ERP_outcome (RelayErpInvoiceAnswer)"),
        new Message("logic", "apim", "Answer_the_Carrier: RECEIVED or APPROVED with each line's result, or a refusal", Dashed: true),
        new Message("apim", "portal", "the same answer", Dashed: true),
        new Message("erp", "pub", "outbox, within a second, when every line matched", Dashed: true),
    ],
};


specs["e2-3"] = new SequenceSpec
{
    Title = "E2 Carrier invoice matching · Finance decides the lines left To decide",
    Subtitle = "Flow carrier-invoice. The Hops as the Integration console shows them, in time order.",
    Footnote = LegendArrows + " Source of truth: docs/processes/erp/e2-carrier-invoice-matching.md, its Message trail.",
    Legend = Legend,
    Groups = [new SequenceGroup("Flow carrier-invoice", ["ui", "pub"])],
    Participants = [
        P("ui", "Accept or reject a line", "green", "erp-web"),
        P("pub", "publish CarrierInvoiceApproved", "green", "erp-api"),
    ],
    Steps = [
        new SelfStep("ui", "AcceptCarrierInvoiceLineCommand or RejectCarrierInvoiceLineCommand: the line decided, its Carrier cost Invoiced if accepted"),
        new SelfStep("ui", "The last line decided: the invoice Approved, with the outbox message, one transaction"),
        new Message("ui", "pub", "outbox, within a second", Dashed: true),
    ],
};


specs["e2-4"] = new SequenceSpec
{
    Title = "E2 Carrier invoice matching · The Carrier is told",
    Subtitle = "Flow carrier-invoices. The Hops as the Integration console shows them, in time order.",
    Footnote = LegendArrows + " Source of truth: docs/processes/erp/e2-carrier-invoice-matching.md, its Message trail.",
    Legend = Legend,
    Groups = [new SequenceGroup("Flow carrier-invoices", ["sub", "logic", "portal", "report"])],
    Participants = [
        P("sub", "subscription carrier-invoice-outcome", "violet", "invoicing-events (topic)"),
        P("logic", "carrier-invoice-outcome", "violet", "logic-carrier-invoice-outcome"),
        P("portal", "POST /carrier/v1/carrier-id/invoice-outcomes", "gray", "partner-portal-web"),
        P("report", "POST /v1/carrier-invoices/carrier-id/number/report-told", "green", "erp-api"),
    ],
    Steps = [
        new Message("sub", "logic", "A CarrierInvoiceApproved message is available", Dashed: true),
        new SelfStep("logic", "Track_the_invoice, Map_the_invoice (MapCarrierInvoiceOutcome), Complete_the_message"),
        new SelfStep("logic", "Get_the_Carrier_endpoint_credential (Key Vault)"),
        new Message("logic", "portal", "Send_to_the_Carrier (the outcome of each line)"),
        new Message("portal", "logic", "200 acknowledged", Dashed: true),
        new Message("logic", "report", "Report_the_outcome_to_the_ERP: Report_the_outcome_told"),
        new Message("report", "logic", "200 (Report_the_outcome_not_delivered instead when the retries are used up)", Dashed: true),
    ],
};


specs["p1-1"] = new SequenceSpec
{
    Title = "P1 Transport Order intake · Message trail",
    Subtitle = "Flow transport-order-intake. The Hops as the Integration console shows them, in time order.",
    Footnote = LegendArrows + " Source of truth: docs/processes/tms/p1-transport-order-intake.md, its Message trail.",
    Legend = Legend,
    Groups = [new SequenceGroup("Flow transport-order-intake", ["portal", "apim", "logic", "tms", "pub"])],
    Participants = [
        P("portal", "Send booking", "gray", "partner-portal-web"),
        P("apim", "Submit transport order", "violet", "apim-norvia"),
        P("logic", "transport-order-intake", "violet", "logic-intake"),
        P("tms", "POST /v1/transport-orders", "blue", "tms-api"),
        P("pub", "publish TransportOrderAccepted", "blue", "tms-api"),
    ],
    Steps = [
        new Message("portal", "apim", "POST /partner/v1/transport-orders (partner booking)"),
        new Message("apim", "logic", "Canonical call, Shipper ID, correlation ID"),
        new SelfStep("logic", "Map_the_booking (IntakeMap)"),
        new Message("logic", "tms", "Submit_to_the_TMS (Canonical Model Transport Order)"),
        new Message("tms", "logic", "201 or 200 (repeat) or 422", Dashed: true),
        new Message("logic", "apim", "ACCEPTED, ALREADY_BOOKED or REJECTED", Dashed: true),
        new Message("apim", "portal", "the same answer", Dashed: true),
        new Message("tms", "pub", "outbox, within a second", Dashed: true),
    ],
};


specs["p11-1"] = new SequenceSpec
{
    Title = "P11 Trip dispatch · Message trail",
    Subtitle = "Flow trip-dispatch. The Hops as the Integration console shows them, in time order.",
    Footnote = LegendArrows + " Source of truth: docs/processes/tms/p11-trip-dispatch.md, its Message trail.",
    Legend = Legend,
    Groups = [new SequenceGroup("Flow trip-dispatch", ["ui", "call", "pub", "app", "drv", "report", "rcall"])],
    Participants = [
        P("ui", "Dispatch Trip", "blue", "tms-web"),
        P("call", "POST /v1/trips/TRIP-0000007/dispatch", "blue", "tms-api"),
        P("pub", "publish TripDispatched", "blue", "tms-api"),
        P("app", "Send_to_the_Driver_app", "violet", "logic-trip-dispatch"),
        P("drv", "POST /v1/trips", "teal", "driver-app"),
        P("report", "Report_the_version_delivered", "violet", "logic-trip-dispatch"),
        P("rcall", "POST /v1/trips/TRIP-0000007/versions/1/report-delivered", "blue", "tms-api"),
    ],
    Steps = [
        new Message("ui", "call", "command"),
        new SelfStep("call", "Trip rules, then the new version and the outbox message in one transaction"),
        new Message("call", "ui", "200, or 404, 409 or 422 and nothing changes", Dashed: true),
        new Message("call", "pub", "outbox, within a second", Dashed: true),
        new Message("pub", "app", "trip-events, subscription trip-dispatch", Dashed: true),
        new Message("app", "drv", "the whole Trip, retried on a timeout, 408, 429 and 5xx"),
        new Message("drv", "app", "2xx, the Trip version kept", Dashed: true),
        new Message("app", "report", "delivered"),
        new Message("report", "rcall", "X-Correlation-ID of the dispatch"),
        new Message("rcall", "report", "2xx, the version is Delivered to Driver app", Dashed: true),
    ],
};


specs["p12-1"] = new SequenceSpec
{
    Title = "P12 Trip execution · Message trail",
    Subtitle = "Flow trip-execution. The Hops as the Integration console shows them, in time order.",
    Footnote = LegendArrows + " Source of truth: docs/processes/tms/p12-trip-execution.md, its Message trail.",
    Legend = Legend,
    Groups = [new SequenceGroup("Flow trip-execution", ["ui", "pub", "lapp", "call", "out", "ocall"])],
    Participants = [
        P("ui", "POST /trips/TRIP-0000007/stops/SHP-0000011/collection/collected", "teal", "driver-app-web"),
        P("pub", "publish StopCompletedReported", "teal", "driver-app-web"),
        P("lapp", "Send_the_report_to_the_TMS", "blue", "logic-driver-reports"),
        P("call", "POST /v1/tracking-events", "blue", "tms-api"),
        P("out", "Report_recorded", "violet", "logic-driver-reports"),
        P("ocall", "POST /v1/report-outcomes", "teal", "driver-app-api"),
    ],
    Steps = [
        new SelfStep("ui", "DriverReport (Sending) and the outbox message in one transaction"),
        new Message("ui", "pub", "outbox, within a second", Dashed: true),
        new Message("pub", "lapp", "driver-app-events, session = Trip number, subscription driver-reports", Dashed: true),
        new Message("lapp", "call", "Own fleet Tracking event, retried on a timeout, 408, 429 and 5xx"),
        new SelfStep("call", "Driver report rules, then Tracking event, Shipment status and outbox in one transaction"),
        new Message("call", "lapp", "2xx, or 404, 409 or 400 and nothing changes", Dashed: true),
        new Message("lapp", "out", "message completed"),
        new Message("out", "ocall", "Recorded"),
        new Message("ocall", "out", "2xx, the report is Recorded", Dashed: true),
    ],
};


specs["p2-1"] = new SequenceSpec
{
    Title = "P2 Rating · Message trail",
    Subtitle = "Flow transport-order-rating. The Hops as the Integration console shows them, in time order.",
    Footnote = LegendArrows + " Source of truth: docs/processes/tms/p2-rating.md, its Message trail.",
    Legend = Legend,
    Groups = [new SequenceGroup("Flow transport-order-rating", ["ui", "tms", "call", "pub"])],
    Participants = [
        P("ui", "Re-rate Transport Order", "blue", "tms-web"),
        P("tms", "Rate Transport Order", "blue", "tms-api"),
        P("call", "POST /v1/transport-orders/TO-0000042/re-rate", "blue", "tms-api"),
        P("pub", "publish TransportOrderRated", "blue", "tms-api"),
    ],
    Steps = [
        new Note(["tms"], "First rating, after the acceptance commit or the next sweep"),
        new SelfStep("tms", "Rate Card version, Lane, Weight Break, Freight Charge"),
        new Message("tms", "pub", "outbox, within a second", Dashed: true),
        new Note(["ui"], "Re-rate, only for an order whose Rating failed"),
        new Message("ui", "call", "re-rate command with an Idempotency-Key"),
        new SelfStep("call", "the same calculation"),
        new Message("call", "ui", "200 with the rating result, or 409 and nothing changes", Dashed: true),
        new Message("call", "pub", "outbox, within a second", Dashed: true),
    ],
};


specs["p3-1"] = new SequenceSpec
{
    Title = "P3 Transport planning · Message trail",
    Subtitle = "Flows transport-planning and carrier-booking. The Hops as the Integration console shows them, in time order.",
    Footnote = LegendArrows + " Source of truth: docs/processes/tms/p3-transport-planning.md, its Message trail.",
    Legend = Legend,
    Groups = [
        new SequenceGroup("Flow transport-planning", ["ui", "call", "pub"]),
        new SequenceGroup("Flow carrier-booking", ["booking"]),
    ],
    Participants = [
        P("ui", "Assign Transport Order", "blue", "tms-web"),
        P("call", "POST /v1/transport-orders/TO-0000042/assign", "blue", "tms-api"),
        P("pub", "publish ShipmentPlanned", "blue", "tms-api"),
        P("booking", "carrier-booking", "violet", "logic-booking"),
    ],
    Steps = [
        new Message("ui", "call", "command with an Idempotency-Key"),
        new SelfStep("call", "assignment rules, then the change and the outbox message in one transaction"),
        new Message("call", "ui", "201 or 200, or 404, 409 or 422 and nothing changes", Dashed: true),
        new Message("call", "pub", "outbox, within a second", Dashed: true),
        new Message("pub", "booking", "shipment-events, subscription carrier-booking", Dashed: true),
        new Note(["booking"], "Takes all three events of a Carrier Shipment, acts on ShipmentReleased only"),
    ],
};


specs["p4-1"] = new SequenceSpec
{
    Title = "P4 Carrier booking · Message trail",
    Subtitle = "Flows transport-planning and carrier-booking and carrier-booking-report. The Hops as the Integration console shows them, in time order.",
    Footnote = LegendArrows + " Source of truth: docs/processes/tms/p4-carrier-booking.md, its Message trail.",
    Legend = Legend,
    Groups = [
        new SequenceGroup("Flow transport-planning", ["ui", "rel", "pub"]),
        new SequenceGroup("Flow carrier-booking", ["booking", "recv"]),
        new SequenceGroup("Flow carrier-booking-report", ["sent"]),
    ],
    Participants = [
        P("ui", "Release Shipment", "blue", "tms-web"),
        P("rel", "POST /v1/shipments/SH-0000042/release", "blue", "tms-api"),
        P("pub", "publish ShipmentReleased", "blue", "tms-api"),
        P("booking", "carrier-booking", "violet", "logic-booking"),
        P("recv", "POST /carrier/v1/carrier-id/bookings", "gray", "partner-portal-web"),
        P("sent", "POST /v1/carrier-bookings/CB-0000042/report-sent", "blue", "tms-api"),
    ],
    Steps = [
        new Message("ui", "rel", "release command"),
        new SelfStep("rel", "Shipment Released and Carrier booking Requested, with the outbox message, in one transaction"),
        new Message("rel", "ui", "201 or 200", Dashed: true),
        new Message("rel", "pub", "outbox, within a second", Dashed: true),
        new Message("pub", "booking", "shipment-events, subscription carrier-booking", Dashed: true),
        new SelfStep("booking", "Map_the_release, Get_the_Carrier_endpoint_credential"),
        new Message("booking", "recv", "Send_to_the_Carrier (Carrier booking format)"),
        new Message("recv", "booking", "200 received", Dashed: true),
        new Message("booking", "sent", "Report_the_booking_sent"),
        new Message("sent", "booking", "200", Dashed: true),
    ],
};


specs["p4-2"] = new SequenceSpec
{
    Title = "P4 Carrier booking · Message trail",
    Subtitle = "Flows carrier-answer and carrier-booking-answer. The Hops as the Integration console shows them, in time order.",
    Footnote = LegendArrows + " Source of truth: docs/processes/tms/p4-carrier-booking.md, its Message trail.",
    Legend = Legend,
    Groups = [
        new SequenceGroup("Flow carrier-answer", ["portal", "apim", "logic"]),
        new SequenceGroup("Flow carrier-booking-answer", ["tms", "pub"]),
    ],
    Participants = [
        P("portal", "Send Carrier answer", "gray", "partner-portal-web"),
        P("apim", "Answer Carrier booking", "violet", "apim-norvia"),
        P("logic", "carrier-answer", "violet", "logic-answer"),
        P("tms", "POST /v1/carrier-bookings/CB-0000042/answer", "blue", "tms-api"),
        P("pub", "publish CarrierBookingConfirmed", "blue", "tms-api"),
    ],
    Steps = [
        new Message("portal", "apim", "POST /partner/v1/carrier-answers (Persona token and subscription key)"),
        new Message("apim", "logic", "Canonical call, Carrier ID from azp, correlation ID"),
        new SelfStep("logic", "Map_the_answer (CarrierAnswerMap)"),
        new Message("logic", "tms", "Answer_to_the_TMS (Canonical Model answer)"),
        new Message("tms", "logic", "200 (also a repeat), 404, 403, 409, 422 or 400", Dashed: true),
        new SelfStep("logic", "Relay_the_TMS_outcome (CarrierAnswerResponses)"),
        new Message("logic", "apim", "RECORDED or a refusal code", Dashed: true),
        new Message("apim", "portal", "the same answer", Dashed: true),
        new Message("tms", "pub", "outbox, within a second", Dashed: true),
    ],
};


specs["p5-1"] = new SequenceSpec
{
    Title = "P5 Shipment tracking · Message trail",
    Subtitle = "Flow carrier-tracking. The Hops as the Integration console shows them, in time order.",
    Footnote = LegendArrows + " Source of truth: docs/processes/tms/p5-shipment-tracking.md, its Message trail.",
    Legend = Legend,
    Groups = [new SequenceGroup("Flow carrier-tracking", ["portal", "apim", "queue", "logic", "tms", "pub"])],
    Participants = [
        P("portal", "Send Tracking event", "gray", "partner-portal-web"),
        P("apim", "Submit Carrier tracking", "violet", "apim-norvia"),
        P("queue", "carrier-tracking (queue)", "violet", ""),
        P("logic", "carrier-tracking", "violet", "logic-tracking"),
        P("tms", "POST /v1/tracking-events", "blue", "tms-api"),
        P("pub", "publish ShipmentDelivered", "blue", "tms-api"),
    ],
    Steps = [
        new Message("portal", "apim", "POST /partner/v1/carrier-tracking (Persona token and subscription key)"),
        new SelfStep("apim", "token, role Tracking.Submit, Carrier ID from azp, correlation ID"),
        new Message("apim", "queue", "Carrier tracking message, with correlationId, flow, partyId, sourceMessageId"),
        new Message("apim", "portal", "202 Accepted", Dashed: true),
        new Message("queue", "logic", "When_a_Carrier_tracking_message_is_available", Dashed: true),
        new SelfStep("logic", "Track_the_event, Map_the_event (CarrierTrackingMap)"),
        new Message("logic", "tms", "Record_in_the_TMS (Canonical Model Tracking event)"),
        new SelfStep("tms", "Tracking event recorded, status and ETA moved, with the outbox message, in one transaction"),
        new Message("tms", "logic", "200 (also a repeat), or 404, 409 or 400", Dashed: true),
        new Message("logic", "queue", "Settle_the_message: complete, dead-letter or abandon"),
        new Message("tms", "pub", "outbox, within a second", Dashed: true),
    ],
};


specs["p6-1"] = new SequenceSpec
{
    Title = "P6 Proof of delivery · Message trail",
    Subtitle = "Flow carrier-proof-of-delivery. The Hops as the Integration console shows them, in time order.",
    Footnote = LegendArrows + " Source of truth: docs/processes/tms/p6-proof-of-delivery.md, its Message trail.",
    Legend = Legend,
    Groups = [new SequenceGroup("Flow carrier-proof-of-delivery", ["portal", "apim", "blob", "queue", "logic", "tms", "pub"])],
    Participants = [
        P("portal", "Send Proof of delivery", "gray", "partner-portal-web"),
        P("apim", "Submit Carrier Proof of delivery", "violet", "apim-norvia"),
        P("blob", "inbound-proofs-of-delivery (blob container)", "violet", ""),
        P("queue", "carrier-proof-of-delivery (queue)", "violet", ""),
        P("logic", "carrier-proof-of-delivery", "violet", "logic-proof-of-delivery"),
        P("tms", "POST /v1/proofs-of-delivery", "blue", "tms-api"),
        P("pub", "publish ProofOfDeliveryReceived", "blue", "tms-api"),
    ],
    Steps = [
        new Message("portal", "apim", "POST /partner/v1/carrier-proofs-of-delivery (Persona token and subscription key)"),
        new SelfStep("apim", "token, role ProofsOfDelivery.Submit, 25 MB limit, Carrier ID from azp, correlation ID"),
        new Message("apim", "blob", "the body, unparsed, as a blob named for the correlation ID"),
        new Message("apim", "queue", "the blob's reference, with correlationId, flow, partyId, sourceMessageId"),
        new Message("apim", "portal", "202 Accepted", Dashed: true),
        new Message("queue", "logic", "When_a_Carrier_Proof_of_delivery_is_available", Dashed: true),
        new Message("logic", "blob", "Read_the_blob"),
        new SelfStep("logic", "Map_the_proof_of_delivery (CarrierProofOfDeliveryMap)"),
        new Message("logic", "tms", "Record_in_the_TMS (multipart: canonical metadata plus files)"),
        new SelfStep("tms", "Proof of delivery recorded as current, any earlier one superseded, Exception opened or joined, Billable decided, with the outbox message, in one transaction"),
        new Message("tms", "logic", "200 (also a repeat), or 404, 409 or 400", Dashed: true),
        new Message("logic", "blob", "Delete_the_blob"),
        new Message("logic", "queue", "Settle_the_message: complete, dead-letter or abandon"),
        new Message("tms", "pub", "outbox, within a second", Dashed: true),
    ],
};


specs["p7-1"] = new SequenceSpec
{
    Title = "P7 Exception handling · Opening",
    Subtitle = "Flow carrier-tracking. The Hops as the Integration console shows them, in time order.",
    Footnote = LegendArrows + " Source of truth: docs/processes/tms/p7-exception-handling.md, its Message trail.",
    Legend = Legend,
    Groups = [new SequenceGroup("Flow carrier-tracking", ["tms", "opened", "reported"])],
    Participants = [
        P("tms", "POST /v1/tracking-events", "blue", "tms-api"),
        P("opened", "publish ExceptionOpened", "blue", "tms-api"),
        P("reported", "publish ShipmentExceptionReported", "blue", "tms-api"),
    ],
    Steps = [
        new SelfStep("tms", "Tracking event recorded, Exception opened or the cause added, with the outbox messages, in one transaction"),
        new Message("tms", "reported", "outbox, within a second", Dashed: true),
        new Message("tms", "opened", "outbox, within a second", Dashed: true),
    ],
};


specs["p7-2"] = new SequenceSpec
{
    Title = "P7 Exception handling · Notifying the Shipper",
    Subtitle = "Flows exception-handling and shipper-notification. The Hops as the Integration console shows them, in time order.",
    Footnote = LegendArrows + " Source of truth: docs/processes/tms/p7-exception-handling.md, its Message trail.",
    Legend = Legend,
    Groups = [
        new SequenceGroup("Flow exception-handling", ["ui", "notify", "pub", "sent"]),
        new SequenceGroup("Flow shipper-notification", ["logic", "recv"]),
    ],
    Participants = [
        P("ui", "Notify Shipper", "blue", "tms-web"),
        P("notify", "POST /v1/exceptions/EX-0000042/notify-shipper", "blue", "tms-api"),
        P("pub", "publish ShipperNotificationRequested", "blue", "tms-api"),
        P("sent", "POST /v1/shipper-notifications/0192f0c1-7a2e-7c3b-9d4e-5a6b7c8d9e0f/report-sent", "blue", "tms-api"),
        P("logic", "shipper-notification", "violet", "logic-shipper-notification"),
        P("recv", "POST /shipper/v1/shipper-id/notifications", "gray", "partner-portal-web"),
    ],
    Steps = [
        new Message("ui", "notify", "notify command (message as edited by the Planner)"),
        new SelfStep("notify", "Shipper notification Requested and the action recorded on the Exception, with the outbox message, in one transaction"),
        new Message("notify", "ui", "200", Dashed: true),
        new Message("notify", "pub", "outbox, within a second", Dashed: true),
        new Message("pub", "logic", "shipment-events, subscription shipper-notification", Dashed: true),
        new SelfStep("logic", "Map_the_notification, Complete_the_message, Get_the_Shipper_endpoint_credential"),
        new Message("logic", "recv", "Send_to_the_Shipper (Shipper notification format)"),
        new Message("recv", "logic", "200 received", Dashed: true),
        new Message("logic", "sent", "Report_the_notification_sent"),
        new Message("sent", "logic", "200", Dashed: true),
    ],
};


specs["p7-3"] = new SequenceSpec
{
    Title = "P7 Exception handling · Cancelling a Carrier booking",
    Subtitle = "Flows exception-handling and carrier-cancellation. The Hops as the Integration console shows them, in time order.",
    Footnote = LegendArrows + " Source of truth: docs/processes/tms/p7-exception-handling.md, its Message trail.",
    Legend = Legend,
    Groups = [
        new SequenceGroup("Flow exception-handling", ["ui", "cancel", "pub", "told"]),
        new SequenceGroup("Flow carrier-cancellation", ["logic", "recv"]),
    ],
    Participants = [
        P("ui", "Cancel Carrier booking", "blue", "tms-web"),
        P("cancel", "POST /v1/exceptions/EX-0000042/cancel-carrier-booking", "blue", "tms-api"),
        P("pub", "publish CarrierBookingCancelled", "blue", "tms-api"),
        P("told", "POST /v1/carrier-bookings/CB-0000042/report-cancellation-told", "blue", "tms-api"),
        P("logic", "carrier-cancellation", "violet", "logic-carrier-cancellation"),
        P("recv", "POST /carrier/v1/carrier-id/cancellations", "gray", "partner-portal-web"),
    ],
    Steps = [
        new Message("ui", "cancel", "cancel command (reason text)"),
        new SelfStep("cancel", "Carrier booking Cancelled, Shipment back to Planned and the action recorded on the Exception, with the outbox message, in one transaction"),
        new Message("cancel", "ui", "200", Dashed: true),
        new Message("cancel", "pub", "outbox, within a second", Dashed: true),
        new Message("pub", "logic", "shipment-events, subscription carrier-cancellation", Dashed: true),
        new SelfStep("logic", "Map_the_cancellation, Complete_the_message, Get_the_Carrier_endpoint_credential"),
        new Message("logic", "recv", "Send_to_the_Carrier (Carrier cancellation format)"),
        new Message("recv", "logic", "200 received", Dashed: true),
        new Message("logic", "told", "Report_the_cancellation_told"),
        new Message("told", "logic", "200", Dashed: true),
    ],
};


specs["p8-1"] = new SequenceSpec
{
    Title = "P8 Shipper visibility · Message trail",
    Subtitle = "Flow shipper-tracking. The Hops as the Integration console shows them, in time order.",
    Footnote = LegendArrows + " Source of truth: docs/processes/tms/p8-shipper-visibility.md, its Message trail.",
    Legend = Legend,
    Groups = [new SequenceGroup("Flow shipper-tracking", ["portal", "apim", "tms"])],
    Participants = [
        P("portal", "Get Transport Order", "gray", "partner-portal-web"),
        P("apim", "Get Transport Order", "violet", "apim-norvia"),
        P("tms", "GET /v1/shipper-tracking/transport-orders/{transportOrderNumber}", "blue", "tms-api"),
    ],
    Steps = [
        new Message("portal", "apim", "GET /partner/v1/transport-orders/{number} (Persona token and subscription key)"),
        new SelfStep("apim", "token, role Tracking.Read, Shipper ID from azp, rate limit, correlation ID"),
        new Message("apim", "tms", "managed identity, X-Shipper-ID, X-Correlation-ID"),
        new Message("tms", "apim", "the Shipper's view (Canonical Model), or 404, or 5xx", Dashed: true),
        new SelfStep("apim", "Liquid map, Canonical to Shipper tracking format"),
        new Message("apim", "portal", "the Shipper tracking format, or 403, 429 or 503", Dashed: true),
    ],
};


specs["p9-1"] = new SequenceSpec
{
    Title = "P9 Freight settlement · Release with a Spot price",
    Subtitle = "Flows transport-planning and carrier-booking. The Hops as the Integration console shows them, in time order.",
    Footnote = LegendArrows + " Source of truth: docs/processes/tms/p9-freight-settlement.md, its Message trail.",
    Legend = Legend,
    Groups = [
        new SequenceGroup("Flow transport-planning", ["ui", "call", "pub"]),
        new SequenceGroup("Flow carrier-booking", ["booking"]),
    ],
    Participants = [
        P("ui", "Release Shipment with Spot price", "blue", "tms-web"),
        P("call", "POST /v1/shipments/SH-0000042/release-with-spot-price", "blue", "tms-api"),
        P("pub", "publish ShipmentReleased", "blue", "tms-api"),
        P("booking", "carrier-booking", "violet", "logic-booking"),
    ],
    Steps = [
        new Message("ui", "call", "Spot price and remark, with an Idempotency-Key"),
        new SelfStep("call", "Carrier Rate Card can't price it, Spot price frozen on the Carrier booking, in one transaction"),
        new Message("call", "pub", "outbox, within a second (carries the Carrier cost)", Dashed: true),
        new Message("pub", "booking", "shipment-events, subscription carrier-booking", Dashed: true),
    ],
};


specs["p9-2"] = new SequenceSpec
{
    Title = "P9 Freight settlement · The Delivered Tracking event",
    Subtitle = "Flows carrier-tracking and billing. The Hops as the Integration console shows them, in time order.",
    Footnote = LegendArrows + " Source of truth: docs/processes/tms/p9-freight-settlement.md, its Message trail.",
    Legend = Legend,
    Groups = [
        new SequenceGroup("Flow carrier-tracking", ["apim", "queue", "logic", "tms", "delivered", "incurred"]),
        new SequenceGroup("Flow billing", ["billable"]),
    ],
    Participants = [
        P("apim", "Submit Carrier tracking", "violet", "apim-norvia"),
        P("queue", "carrier-tracking (queue)", "violet", ""),
        P("logic", "carrier-tracking", "violet", "logic-tracking"),
        P("tms", "POST /v1/tracking-events", "blue", "tms-api"),
        P("delivered", "publish ShipmentDelivered", "blue", "tms-api"),
        P("incurred", "publish CarrierCostIncurred", "blue", "tms-api"),
        P("billable", "publish TransportOrderBillable", "blue", "tms-api"),
    ],
    Steps = [
        new Message("apim", "queue", "Carrier tracking message (POD), 202 Accepted to the Carrier"),
        new Message("queue", "logic", "When_a_Carrier_tracking_message_is_available", Dashed: true),
        new Message("logic", "tms", "Record_in_the_TMS"),
        new SelfStep("tms", "Shipment Delivered, Carrier cost incurred, Billable decided, with the outbox messages, in one transaction"),
        new Message("tms", "delivered", "outbox, within a second", Dashed: true),
        new Message("tms", "incurred", "outbox, within a second, Carrier Shipments only", Dashed: true),
        new Message("tms", "billable", "outbox, within a second, when this made the last condition true", Dashed: true),
    ],
};


specs["p9-3"] = new SequenceSpec
{
    Title = "P9 Freight settlement · The Proof of delivery",
    Subtitle = "Flows carrier-proof-of-delivery and billing. The Hops as the Integration console shows them, in time order.",
    Footnote = LegendArrows + " Source of truth: docs/processes/tms/p9-freight-settlement.md, its Message trail.",
    Legend = Legend,
    Groups = [
        new SequenceGroup("Flow carrier-proof-of-delivery", ["apim", "queue", "logic", "tms"]),
        new SequenceGroup("Flow billing", ["billable"]),
    ],
    Participants = [
        P("apim", "Submit Carrier Proof of delivery", "violet", "apim-norvia"),
        P("queue", "carrier-proof-of-delivery (queue)", "violet", ""),
        P("logic", "carrier-proof-of-delivery", "violet", "logic-proof-of-delivery"),
        P("tms", "POST /v1/proofs-of-delivery", "blue", "tms-api"),
        P("billable", "publish TransportOrderBillable", "blue", "tms-api"),
    ],
    Steps = [
        new Message("apim", "queue", "the blob's reference, 202 Accepted to the Carrier"),
        new Message("queue", "logic", "When_a_Carrier_Proof_of_delivery_is_available", Dashed: true),
        new Message("logic", "tms", "Record_in_the_TMS (multipart: canonical metadata plus files)"),
        new SelfStep("tms", "Proof of delivery recorded, Billable decided, with the outbox message, in one transaction"),
        new Message("tms", "billable", "outbox, within a second, when this made the last condition true", Dashed: true),
    ],
};


specs["p9-4"] = new SequenceSpec
{
    Title = "P9 Freight settlement · Customer service's release",
    Subtitle = "Flow billing. The Hops as the Integration console shows them, in time order.",
    Footnote = LegendArrows + " Source of truth: docs/processes/tms/p9-freight-settlement.md, its Message trail.",
    Legend = Legend,
    Groups = [new SequenceGroup("Flow billing", ["ui", "call", "pub"])],
    Participants = [
        P("ui", "Release without Proof of delivery", "blue", "tms-web"),
        P("call", "POST /v1/transport-orders/TO-0000042/release-without-proof-of-delivery", "blue", "tms-api"),
        P("pub", "publish TransportOrderBillable", "blue", "tms-api"),
    ],
    Steps = [
        new Message("ui", "call", "remark and who released it"),
        new SelfStep("call", "release recorded, Billable decided, with the outbox message, in one transaction"),
        new Message("call", "pub", "outbox, within a second, when the release made the last condition true", Dashed: true),
    ],
};

DiagramOutput.RenderAll(specs, DiagramOutput.SpecDirectory(), "message-trail-", args, SequenceDiagram.Render);
