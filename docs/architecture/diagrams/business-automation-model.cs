#:project ../../../src/Docs/Norvia.Docs.Diagrams/Norvia.Docs.Diagrams.csproj

// The business automation model: which application performs each TMS and ERP process step.
//
//     dotnet run docs/architecture/diagrams/business-automation-model.cs               # writes business-automation-model.excalidraw
//     dotnet run docs/architecture/diagrams/business-automation-model.cs -- preview.svg  # also writes an SVG to check the layout

using Norvia.Docs.Diagrams;

IReadOnlyList<MatrixStep> steps = [
    // id, col, lane, slot, kind, title, sub
    new MatrixStep("a1", 0, "portal", 0, "person", "Submit a booking", "Shipper Persona, Partner API"),
    new MatrixStep("a2", 0, "int", 0, "auto", "Map to the Canonical Model", "P1 · APIM + intake Logic App"),
    new MatrixStep("a3", 0, "tms", 0, "auto", "Accept, or reject with every reason", "P1 · TMS intake API"),
    new MatrixStep("w1", 0, "wms", 0, "planned", "Fulfilment handoff", "WMS to intake, later"),

    new MatrixStep("b1", 1, "tms", 0, "auto", "Rate from the Shipper's Rate Card", "P2 · Freight Charge frozen"),
    new MatrixStep("b2", 1, "tms", 1, "person", "Assign to Own fleet or a Carrier", "P3 · Planner, with load check"),
    new MatrixStep("b3", 1, "tms", 2, "person", "Release the Shipment", "P3 · Planner"),
    new MatrixStep("e0", 1, "erp", 0, "planned", "Rate Cards as master data", "Seed file today, E3 sync later"),

    new MatrixStep("c1", 2, "portal", 0, "person", "Confirm or reject the booking", "P4 · Carrier Persona"),
    new MatrixStep("c2", 2, "int", 0, "auto", "Send the request, relay the answer", "P4 · two Logic Apps"),
    new MatrixStep("c3", 2, "tms", 0, "auto", "Booking Confirmed, Carrier cost frozen", "P4 · P9 · Carrier booking API"),

    new MatrixStep("d1", 3, "tms", 0, "auto", "Open the day's capacity", "P10 · Rosters, hour budgets"),
    new MatrixStep("d2", 3, "tms", 1, "person", "Build the Trip and dispatch it", "P11 · Planner"),
    new MatrixStep("d3", 3, "int", 0, "auto", "Send the Trip as a whole version", "P11 · Trip dispatch Logic App"),
    new MatrixStep("d4", 3, "drv", 0, "auto", "Receive the Trip", "P11 · Driver app Trip endpoint"),

    new MatrixStep("f2", 4, "portal", 0, "person", "Report collected, in transit, delivered", "P5 · Carrier Persona"),
    new MatrixStep("f3", 4, "int", 0, "auto", "Put the reports in order and relay", "P5 · P12 · Logic Apps, per Trip"),
    new MatrixStep("f4", 4, "tms", 0, "auto", "Move status and ETA forward", "P5 · Tracking events, Stale flag"),
    new MatrixStep("f1", 4, "drv", 0, "person", "Start the Trip, complete each Stop", "P12 · Driver, captures the Proof"),

    new MatrixStep("g1", 5, "portal", 0, "person", "Send the signed Proof of delivery", "P6 · Carrier Persona"),
    new MatrixStep("g2", 5, "int", 0, "auto", "Claim-check the files and relay", "P6 · Proof of delivery Logic App"),
    new MatrixStep("g3", 5, "tms", 0, "auto", "Store the current Proof", "P6 · earlier one superseded"),

    new MatrixStep("h1", 6, "portal", 0, "person", "Ask where a Transport Order stands", "P8 · Shipper Persona"),
    new MatrixStep("h2", 6, "int", 0, "auto", "Map the Tracking status", "P8 · APIM tracking API + policy"),
    new MatrixStep("h3", 6, "tms", 0, "auto", "Read status, ETA and Proof", "P8 · TMS Shipper tracking API"),

    new MatrixStep("i4", 7, "portal", 0, "auto", "Receive the message", "P7 · Shipper and Carrier Personas"),
    new MatrixStep("i3", 7, "int", 0, "auto", "Notify the Shipper, cancel the booking", "P7 · two Logic Apps"),
    new MatrixStep("i1", 7, "tms", 0, "auto", "Open an Exception", "P7 · failed, damaged, late"),
    new MatrixStep("i2", 7, "tms", 1, "person", "Resolve: re-plan, notify, cancel", "P7 · Planner"),

    new MatrixStep("j2", 8, "int", 0, "auto", "Hand over the billable line", "P9 · Billing Logic App"),
    new MatrixStep("j1", 8, "tms", 0, "auto", "Mark the order Billable", "P9 · Delivered, Rated, with Proof"),
    new MatrixStep("j3", 8, "erp", 0, "auto", "Keep the line Waiting", "E1 · ERP billing API"),

    new MatrixStep("m3", 9, "portal", 0, "auto", "Invoice lands in the inbox", "E1 · Shipper Persona"),
    new MatrixStep("m2", 9, "int", 0, "auto", "Send the Shipper invoice", "E1 · Shipper invoice Logic App"),
    new MatrixStep("m1", 9, "erp", 0, "auto", "Issue one invoice per Shipper", "E1 · monthly run or Invoice now"),
    new MatrixStep("m0", 9, "erp", 1, "person", "Correct with a Credit note", "E1 · Finance"),

    new MatrixStep("k1", 10, "portal", 0, "person", "Submit the Carrier invoice", "E2 · Carrier Persona"),
    new MatrixStep("k2", 10, "int", 0, "auto", "Relay the invoice and each line's outcome", "E2 · two Logic Apps"),
    new MatrixStep("k3", 10, "erp", 0, "auto", "Match each line to its Carrier cost", "E2 · cost frozen at booking, P4"),
    new MatrixStep("k4", 10, "erp", 1, "person", "Decide the lines that don't match", "E2 · Finance"),
];

IReadOnlyList<MatrixEdge> edges = [
    new MatrixEdge("a1", "a2", "v"),
    new MatrixEdge("a2", "a3", "v"),
    new MatrixEdge("w1", "a2", "gl", Dashed: true),
    new MatrixEdge("a3", "b1", "h"),
    new MatrixEdge("b1", "b2", "v"),
    new MatrixEdge("b2", "b3", "v"),
    new MatrixEdge("e0", "b1", "gl", Dashed: true, Off: 14, Ddy: 14),
    new MatrixEdge("b3", "c2", "h"),
    new MatrixEdge("c2", "c1", "v", Both: true),
    new MatrixEdge("c2", "c3", "v"),
    new MatrixEdge("b3", "d2", "h"),
    new MatrixEdge("d1", "d2", "v"),
    new MatrixEdge("d2", "d3", "gr", Off: 10, Ddy: -14),
    new MatrixEdge("d3", "d4", "gr", Off: 22, Sdy: 14),
    new MatrixEdge("c3", "f2", "h"),
    new MatrixEdge("d4", "f1", "h"),
    new MatrixEdge("f2", "f3", "v"),
    new MatrixEdge("f1", "f3", "gr", Off: 12),
    new MatrixEdge("f3", "f4", "v"),
    new MatrixEdge("g1", "g2", "v"),
    new MatrixEdge("g2", "g3", "v"),
    new MatrixEdge("h1", "h2", "v", Both: true),
    new MatrixEdge("h2", "h3", "v", Both: true),
    new MatrixEdge("i1", "i2", "v"),
    new MatrixEdge("i2", "i3", "gr", Off: 12),
    new MatrixEdge("i3", "i4", "v"),
    new MatrixEdge("j1", "j2", "v"),
    new MatrixEdge("j2", "j3", "gl", Off: 12),
    new MatrixEdge("j3", "m1", "h"),
    new MatrixEdge("m1", "m2", "v"),
    new MatrixEdge("m2", "m3", "v"),
    new MatrixEdge("m1", "m0", "v"),
    new MatrixEdge("k1", "k2", "v", Both: true),
    new MatrixEdge("k2", "k3", "v"),
    new MatrixEdge("k3", "k4", "v"),
];


var spec = new MatrixSpec
{
    Title = "Norvia Logistics · Business automation model",
    Subtitle = "Which application performs each business process step. Rows are applications, columns are the TMS and ERP business processes. Every hand-off between applications runs through the integration layer.",
    Footnote = "Solid arrows are built flows, dashed grey arrows are planned or reference data. Sources: docs/processes (P1–P12, E1–E2), docs/architecture/c4.md, GLOSSARY.md and ADRs 0001, 0002 and 0012.",
    Lanes = [
        new MatrixLane("portal", "gray", "Partner portal", "Shippers and Carriers,\none Persona per party"),
        new MatrixLane("int", "violet", "Integration layer", "API Management,\nLogic Apps, Service Bus"),
        new MatrixLane("tms", "blue", "TMS", "Planner and\nCustomer service"),
        new MatrixLane("erp", "green", "ERP", "Finance"),
        new MatrixLane("drv", "teal", "Driver app", "Driver, on the road"),
        new MatrixLane("wms", "lightgray", "WMS", "Not built yet", Dashed: true),
    ],
    Bands = [
        new MatrixBand("Order to delivery", "blue", [new("Receive the order", "P1"), new("Rate and plan", "P2 · P3"), new("Book the Carrier", "P4 · Carrier only"), new("Dispatch the Trip", "P10 · P11 · Own fleet only"), new("Execute and track", "P5 · P12"), new("Prove delivery", "P6"), new("Show the Shipper", "P8 · any time"), new("Resolve exceptions", "P7")]),
        new MatrixBand("Order to cash", "green", [new("Hand over to the ERP", "P9"), new("Invoice the Shipper", "E1")]),
        new MatrixBand("Carrier procure to pay", "orange", [new("Settle the Carrier", "E2")]),
    ],
    Cross = [
        new MatrixCross("Integration console", "Integration operator", "pink", "Watches every hop of every process above, on one Message trail per correlation ID", "Finds the trail, shows failed Logic App runs and dead-lettered messages, and resubmits or discards them. It reads the estate's telemetry and stores nothing."),
        new MatrixCross("Simulator", "Presenter", "orange", "Plays the outside world and Norvia's people through the estate's real doors", "Books as each Shipper, answers and tracks as each Carrier, plans as the Planner, drives as each Driver and invoices as Finance, so every run leaves a Message trail."),
    ],
    Steps = steps,
    Edges = edges,
    EdgeLabels = [new MatrixLabel("Own fleet", "d2", -64, -22)],
};

MatrixDiagram.Render(spec, Path.Combine(DiagramOutput.SpecDirectory(), "business-automation-model.excalidraw"), args.Length > 0 ? args[0] : null);
