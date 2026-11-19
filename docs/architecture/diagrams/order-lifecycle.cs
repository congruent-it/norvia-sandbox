#:project ../../../src/Docs/Norvia.Docs.Diagrams/Norvia.Docs.Diagrams.csproj

// The life of an order across the estate, as four sequence diagrams.
//
//     dotnet run docs/architecture/diagrams/order-lifecycle.cs -- [name ...]    # writes order-lifecycle-<name>.excalidraw and .png; no names draws all
//
// Sequence style (see README.md). The PNGs need rsvg-convert.

using Norvia.Docs.Diagrams;

static LegendItem L(string label, string key) => Palette.Legend(label, key);
static SequenceParticipant P(string id, string title, string pal = "violet", string sub = "", string? kind = null) => new(id, title, pal, sub, kind);
LegendItem[] Legend = [L("Outside Norvia", "gray"), L("Integration layer", "violet"), L("TMS", "blue"), L("ERP", "green"), Palette.LegendPerson()];
const string LegendArrows = "Solid arrows are calls, dashed arrows are replies and events. Every call between applications goes through the integration layer.";
var Shipper = P("shipper", "Shipper Persona", "gray", "Partner portal");
var Carrier = P("carrier", "Carrier Persona", "gray", "Partner portal");
var Apim = P("apim", "API Management", "violet", "Norvia Partner API");
var Bus = P("bus", "Service Bus", "violet", "topics and queues");
var Tms = P("tms", "TMS", "blue", "TMS API");
var Erp = P("erp", "ERP", "green", "ERP API");
var Planner = P("planner", "Planner", "blue", "TMS UI", kind: "person");
var Driver = P("driver", "Driver", "teal", "Driver app UI", kind: "person");
var Driverapp = P("app", "Driver app", "teal", "Driver app API");
var Finance = P("finance", "Finance", "green", "ERP UI", kind: "person");

var specs = new Dictionary<string, SequenceSpec>();

specs["booked"] = new SequenceSpec
{
    Title = "Order lifecycle 1 · From a booking to a Booked Shipment, with a Carrier",
    Subtitle = "A Shipper books, the TMS accepts and rates the order, the Planner assigns and releases it, and the Carrier confirms. P1 to P4. An Own fleet Shipment leaves this path at the release: see lifecycles 2 and 3.",
    Footnote = LegendArrows + " Source of truth: docs/processes/tms (P1 to P4) and docs/architecture/c4.md.",
    Legend = Legend,
    Participants = [
        Shipper,
        Apim,
        P("intake", "Intake Logic App"),
        Tms,
        Planner,
        Bus,
        P("booking", "Carrier booking Logic App"),
        Carrier,
        P("answer", "Carrier answer Logic App"),
    ],
    Steps = [
        new Phase("P1 · Transport Order intake"),
        new Message("shipper", "apim", "POST /partner/v1/transport-orders: the booking, in the partner's format"),
        new Message("apim", "intake", "Canonical call, with the Shipper from the token and a correlation ID"),
        new Message("intake", "tms", "Submit the Transport Order, mapped to the Canonical Model"),
        new SelfStep("tms", "Accepts it with a Transport Order number, or rejects it with every reason. The event goes to the outbox in the same transaction"),
        new Message("tms", "intake", "201 accepted, 200 repeat, or 422 rejected", Dashed: true),
        new Message("intake", "apim", "ACCEPTED, ALREADY_BOOKED or REJECTED", Dashed: true),
        new Message("apim", "shipper", "The same answer, in the same call", Dashed: true),
        new Message("tms", "bus", "TransportOrderAccepted, within a second", Dashed: true),
        new Phase("P2 · Rating and P3 · Transport planning"),
        new SelfStep("tms", "The rating worker rates the order from the Shipper's Rate Card and freezes the Freight Charge. A pricing gap never rejects the booking"),
        new Message("planner", "tms", "Assign the Transport Order to a Carrier"),
        new SelfStep("tms", "Checks the assignment rules, makes the Shipment and publishes ShipmentPlanned"),
        new Message("planner", "tms", "Release the Shipment, with a Spot price if no Carrier Rate Card prices it"),
        new SelfStep("tms", "Shipment Released, Carrier booking Requested and Carrier cost frozen, in one transaction"),
        new Note(["planner", "tms"], "Own fleet: the Shipment is assigned to Own fleet instead, and released without a Carrier booking or a Carrier cost. The Planner then builds a Trip (lifecycle 2)"),
        new Message("tms", "bus", "ShipmentReleased, carrying the Carrier cost", Dashed: true),
        new Phase("P4 · Carrier booking"),
        new Message("bus", "booking", "Subscription carrier-booking: the Shipment is assigned to a Carrier", Dashed: true),
        new Message("booking", "carrier", "Booking request, in the Carrier's format, with its endpoint credential from Key Vault"),
        new Message("carrier", "booking", "200 received", Dashed: true),
        new Message("booking", "tms", "Report the booking sent"),
        new Message("carrier", "answer", "POST /partner/v1/carrier-answers, through API Management: confirm"),
        new Message("answer", "tms", "Answer the Carrier booking, mapped to the Canonical Model"),
        new SelfStep("tms", "Carrier booking Confirmed and Shipment Booked, in one transaction"),
        new Message("tms", "bus", "CarrierBookingConfirmed", Dashed: true),
    ],
};


specs["own-fleet-dispatch"] = new SequenceSpec
{
    Title = "Order lifecycle 2 · From a booking to a Dispatched Trip, with the Own fleet",
    Subtitle = "After intake and rating (lifecycle 1), the Planner assigns the order to the Own fleet, releases it, builds a Trip and dispatches it to the Driver. P3, P10 and P11.",
    Footnote = LegendArrows + " Source of truth: docs/processes/tms (P3, P10, P11).",
    Legend = [.. Legend, L("Driver app", "teal")],
    Participants = [
        Planner,
        Tms,
        Bus,
        P("dispatch", "Trip dispatch Logic App"),
        Driverapp,
    ],
    Steps = [
        new Phase("P3 · Assign to the Own fleet and release"),
        new Message("planner", "tms", "Assign the Transport Order to the Own fleet"),
        new SelfStep("tms", "Checks the assignment rules and makes the Shipment. The load check for its collection day comes back as a warning, never a refusal (P10)"),
        new Message("planner", "tms", "Release the Shipment: no Carrier booking and no Carrier cost"),
        new SelfStep("tms", "Shipment Released. It now appears in the Shipments to put on Trips"),
        new Note(["planner", "tms"], "P10 keeps the Own fleet's availability: each day of the Planning horizon holds the Vehicles and Drivers available from the Rosters, Absences and Off the road days, and each Driver's Driving-hour budget. A daily job opens each new day"),
        new Phase("P11 · Trip dispatch"),
        new Message("planner", "tms", "Create a Draft Trip: a Vehicle and Driver free that day, a Planned start inside the Driver's Shift, the driving hours, and the Shipments in the order to visit"),
        new SelfStep("tms", "The Draft holds its Vehicle and Driver, so neither has another Trip that day. Nothing is published. The Planner can change it or discard it"),
        new Message("planner", "tms", "Dispatch the Trip"),
        new SelfStep("tms", "The Trip is Dispatched as version 1 and each Shipment Dispatched, in one transaction. A Trip is never deleted once dispatched"),
        new Message("tms", "bus", "TripDispatched, with the whole Trip and its Stops in order", Dashed: true),
        new Message("bus", "dispatch", "Subscription trip-dispatch", Dashed: true),
        new Message("dispatch", "app", "The whole Trip, retried on a timeout, 408, 429 and 5xx. Endpoint key from Key Vault"),
        new SelfStep("app", "Keeps the version. A repeat, or a lower version after a higher one, is acknowledged and ignored"),
        new Message("app", "dispatch", "2xx, the Trip version kept", Dashed: true),
        new Message("dispatch", "tms", "Report the version delivered"),
        new SelfStep("tms", "The version is Delivered to the Driver app. If the Logic App gives up, it is Not delivered and the Planner sees it and sends it again"),
        new Note(["planner", "dispatch"], "Amend sends the Driver a new version of the whole Trip. Once the Trip is under way, the only change allowed is taking off a Shipment that has not been collected, which puts it back to Planned"),
    ],
};


specs["own-fleet-road"] = new SequenceSpec
{
    Title = "Order lifecycle 3 · The Driver on the road, with the Own fleet",
    Subtitle = "The Driver reports, the TMS decides. Every report travels as an event in order per Trip, and the Shipment ends Delivered or goes back to planning. P12.",
    Footnote = LegendArrows + " Source of truth: docs/processes/tms/p12-trip-execution.md.",
    Legend = [.. Legend, L("Driver app", "teal")],
    Participants = [
        Driver,
        Driverapp,
        P("bus", "Service Bus", "violet", "driver-app-events topic"),
        P("reports", "Driver reports Logic App"),
        Tms,
    ],
    Steps = [
        new Phase("Start the Trip"),
        new Message("driver", "app", "Start Trip, on the Trip's day"),
        new SelfStep("app", "Saves the report as Sending and its event in the outbox, in one transaction"),
        new Message("app", "bus", "TripStartReported, in the session of the Trip's number", Dashed: true),
        new Message("bus", "reports", "Subscription driver-reports: one Trip's reports are read one at a time, in order", Dashed: true),
        new Message("reports", "tms", "Start a Trip"),
        new SelfStep("tms", "The Trip is In progress"),
        new Message("reports", "app", "Report the outcome: Recorded, or Refused with the TMS's code"),
        new Phase("Collect and deliver"),
        new Message("driver", "app", "Collected at a collection Stop"),
        new Message("app", "bus", "StopCompletedReported, with a Collected Tracking event", Dashed: true),
        new Message("bus", "reports", "The next report in the Trip's session", Dashed: true),
        new Message("reports", "tms", "Record the Tracking event, as an Own fleet report"),
        new SelfStep("tms", "The Shipment is Collected. Its ETA is its delivery window, since the Driver app sends no GPS and no planned times"),
        new Message("driver", "app", "Deliver: received by, the condition, a signature and up to four photos"),
        new Message("app", "bus", "StopCompletedReported and ProofOfDeliveryCaptured. The files wait in the transit container", Dashed: true),
        new Message("reports", "tms", "Record the Delivered event, then the Proof of delivery with its files"),
        new SelfStep("tms", "The Shipment is Delivered and its Proof of delivery is the current one. Billable is decided in the same transaction"),
        new Note(["driver", "app"], "A Stop that fails is reported as Collection failed or Delivery failed, with a remark. The TMS opens an Exception for it, and the Planner resolves it as in lifecycle 4"),
        new Phase("End the Trip"),
        new Message("driver", "app", "End Trip, once every Stop has an outcome"),
        new Message("app", "bus", "TripEndReported, after the last Stop report in the session", Dashed: true),
        new Message("reports", "tms", "End a Trip"),
        new SelfStep("tms", "The Trip is Completed. A Shipment not collected goes back to Planned, and one collected but not delivered is Awaiting re-delivery"),
        new Note(["app", "tms"], "A Shipment Awaiting re-delivery is still on Norvia's Vehicle. It goes on a later Trip with only its delivery Stop (P11, P12)"),
    ],
};


specs["delivered"] = new SequenceSpec
{
    Title = "Order lifecycle 4 · From Booked to Delivered by a Carrier, and what everyone sees",
    Subtitle = "The Carrier reports tracking and a Proof of delivery, the TMS moves the Shipment forward, and the Shipper reads where it stands. P5 to P8. The Shipper and the Planner see the same whoever carries it.",
    Footnote = LegendArrows + " Source of truth: docs/processes/tms (P5 to P8).",
    Legend = Legend,
    Participants = [
        Shipper,
        Carrier,
        Apim,
        P("bus", "Service Bus and storage", "violet", "queues and a blob"),
        P("tracking", "Carrier tracking Logic App"),
        P("pod", "Carrier Proof of delivery Logic App"),
        Tms,
        Planner,
    ],
    Steps = [
        new Phase("P5 · Shipment tracking"),
        new Message("carrier", "apim", "POST /partner/v1/carrier-tracking: collected, in transit or delivered"),
        new Message("apim", "bus", "The message on the carrier-tracking queue, after the token and role checks"),
        new Message("apim", "carrier", "202 Accepted", Dashed: true),
        new Message("bus", "tracking", "A tracking message is available", Dashed: true),
        new Message("tracking", "tms", "Record the Tracking event, mapped from the Carrier's status code"),
        new SelfStep("tms", "Records the event and moves the Shipment's status and ETA forward, never backwards. The event goes to the outbox in the same transaction"),
        new Message("tms", "tracking", "200, also for a repeat", Dashed: true),
        new Message("tracking", "bus", "Settle the message: complete, dead-letter or abandon", Dashed: true),
        new Note(["carrier", "tms"], "Own fleet Shipments: the Driver reports the same Tracking events and Proofs of delivery through the Driver app, and they reach the TMS through the Driver reports Logic App (lifecycle 3)"),
        new Phase("P6 · Proof of delivery"),
        new Message("carrier", "apim", "POST /partner/v1/carrier-proofs-of-delivery: a signed CMR, a signature or photos"),
        new Message("apim", "bus", "Stores the file as a blob and queues its reference (claim check)"),
        new Message("apim", "carrier", "202 Accepted", Dashed: true),
        new Message("bus", "pod", "A Proof of delivery reference is available", Dashed: true),
        new Message("pod", "bus", "Reads the blob, and deletes it once recorded"),
        new Message("pod", "tms", "Record the Proof of delivery: canonical metadata plus the files"),
        new SelfStep("tms", "Stores it as the current one, supersedes any earlier one, opens or joins an Exception for a reservation, and decides whether the order is Billable"),
        new Phase("P8 · What the Shipper sees"),
        new Message("shipper", "apim", "GET /partner/v1/transport-orders/{number}"),
        new Message("apim", "tms", "Shipper tracking query, as API Management's identity"),
        new Message("tms", "apim", "Tracking status, progress, ETA and the current Proof of delivery", Dashed: true),
        new Message("apim", "shipper", "The same, mapped to the partner format", Dashed: true),
        new Phase("P7 · When something goes wrong"),
        new SelfStep("tms", "A failed collection or delivery, damage, a Proof of delivery with reservations or a Late delivery opens an Exception"),
        new Message("planner", "tms", "Resolve it: re-plan, notify the Shipper, cancel the Carrier booking, or agree a new delivery window"),
        new Note(["pod", "planner"], "Notifying the Shipper and cancelling a booking go out through the Shipper notification and Carrier cancellation Logic Apps, and the TMS records what became of each"),
    ],
};


specs["invoice"] = new SequenceSpec
{
    Title = "Order lifecycle 5 · From delivered to a Shipper invoice",
    Subtitle = "The TMS decides when an order is Billable, and the ERP does everything after that. P9 and E1. It is the same whether a Carrier or the Own fleet carried the goods.",
    Footnote = LegendArrows + " Source of truth: docs/processes/tms/p9-freight-settlement.md and docs/processes/erp/e1-invoicing.md.",
    Legend = Legend,
    Participants = [
        Tms,
        Bus,
        P("billing", "Billing Logic App"),
        Erp,
        Finance,
        P("invlogic", "Shipper invoice Logic App"),
        Shipper,
    ],
    Steps = [
        new Phase("P9 · Freight settlement"),
        new SelfStep("tms", "Delivered, Rated, with a current Proof of delivery and no open Exception: the order becomes Billable. Decided once, in the transaction of the event that completes it"),
        new Message("tms", "bus", "TransportOrderBillable, with the frozen Freight Charge", Dashed: true),
        new Message("bus", "billing", "Subscription billing", Dashed: true),
        new Message("billing", "erp", "Record the Billable line, as the event carries it"),
        new SelfStep("erp", "Billable line Waiting. Idempotent on the Transport Order number, and the Freight Charge is never recalculated"),
        new Message("erp", "billing", "201, or 200 for a repeat", Dashed: true),
        new Phase("E1 · Invoicing"),
        new Message("finance", "erp", "Invoice now for one Shipper. The monthly Invoice run does it for all of them"),
        new SelfStep("erp", "Issues one Shipper invoice, INV-year-number, with a line per Transport Order, and marks those lines Invoiced"),
        new Message("erp", "bus", "ShipperInvoiceIssued, through the ERP's outbox", Dashed: true),
        new Message("bus", "invlogic", "Subscription shipper-invoice", Dashed: true),
        new Message("invlogic", "shipper", "The invoice, in the Shipper's format, to the Persona's invoice endpoint"),
        new Message("shipper", "invlogic", "Acknowledged", Dashed: true),
        new Message("invlogic", "erp", "Report the invoice Sent"),
        new Note(["erp", "finance"], "Finance corrects an issued invoice with a Credit note, which takes the same path as CreditNoteIssued. An Own fleet order is invoiced the same way and has no Carrier cost, so lifecycle 6 does not apply to it"),
    ],
};


specs["carrier-invoice"] = new SequenceSpec
{
    Title = "Order lifecycle 6 · From a Carrier cost to a Carrier invoice outcome",
    Subtitle = "Carrier Shipments only. The TMS prices the Carrier booking and hands the cost over, the ERP matches the Carrier's invoice against it, and the Carrier is told. P9 and E2.",
    Footnote = LegendArrows + " Source of truth: docs/processes/tms/p9-freight-settlement.md and docs/processes/erp/e2-carrier-invoice-matching.md.",
    Legend = Legend,
    Participants = [
        Tms,
        Bus,
        P("costla", "Carrier cost Logic App"),
        Erp,
        P("outla", "Carrier invoice outcome Logic App"),
        Finance,
        P("cinvla", "Carrier invoice Logic App"),
        Carrier,
    ],
    Steps = [
        new Phase("P4 and P9 · The Carrier cost"),
        new SelfStep("tms", "At release the TMS prices the Carrier booking from the Carrier Rate Card, or takes a Spot price, and freezes it on the booking"),
        new SelfStep("tms", "When the Carrier Shipment is Delivered, the TMS publishes CarrierCostIncurred in the same transaction"),
        new Message("tms", "bus", "CarrierCostIncurred", Dashed: true),
        new Message("bus", "costla", "Subscription carrier-cost", Dashed: true),
        new Message("costla", "erp", "Record the Carrier cost, as the event carries it"),
        new SelfStep("erp", "Carrier cost Open. Idempotent on the Carrier booking number"),
        new Phase("E2 · Carrier invoice matching"),
        new Message("carrier", "cinvla", "POST /partner/v1/carrier-invoices, through API Management: one line per Carrier booking"),
        new Message("cinvla", "erp", "Receive the Carrier invoice, mapped to the Canonical Model"),
        new SelfStep("erp", "Matches each line to the Carrier cost of its Carrier booking, within the Matching tolerance. A line that doesn't match is To decide"),
        new Message("erp", "cinvla", "Received or Approved, with each line's result", Dashed: true),
        new Message("cinvla", "carrier", "The same answer, in the same call", Dashed: true),
        new Note(["erp", "finance"], "Lines To decide wait for Finance, who accepts them at the invoiced or an adjusted amount, or rejects them with a reason. An unknown booking is matched again when its Carrier cost arrives"),
        new Message("finance", "erp", "Accept or reject the lines to decide"),
        new SelfStep("erp", "Approved once no line is left to decide. CarrierInvoiceApproved goes to the outbox in the same transaction"),
        new Message("erp", "bus", "CarrierInvoiceApproved", Dashed: true),
        new Message("bus", "outla", "Subscription carrier-invoice-outcome", Dashed: true),
        new Message("outla", "carrier", "The outcome of each line, to the Persona's invoice outcome endpoint"),
        new Message("outla", "erp", "Report the Carrier told"),
    ],
};

DiagramOutput.RenderAll(specs, DiagramOutput.SpecDirectory(), "order-lifecycle-", args, SequenceDiagram.Render);
