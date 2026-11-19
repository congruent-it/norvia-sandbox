#:project ../../../src/Docs/Norvia.Docs.Diagrams/Norvia.Docs.Diagrams.csproj

// The bounded-context diagrams: the context map, the TMS hexagon and the TMS lifecycles.
//
//     dotnet run docs/architecture/diagrams/bounded-contexts.cs -- [name ...]    # writes <name>.excalidraw and .png; no names draws them all
//
// Graph style (see README.md). The PNGs need rsvg-convert.

using Norvia.Docs.Diagrams;

const string Core = "blue", Use = "teal", Adapt = "violet", Drive = "orange";
const string S = "blue", End = "green", Off = "gray";

static LegendItem L(string label, string key) => Palette.Legend(label, key);
var LegPerson = Palette.LegendPerson();
var LegPlanned = Palette.LegendPlanned();
var specs = new Dictionary<string, GraphSpec>();

const string StdFoot = "Source of truth: src/{app}, docs/coding-guidelines.md section 1 and the architecture tests in tests/{app}. Use case counts are the handlers in each Application feature folder.";
var Shared = ("Persistence", "IRepository<T>, IReadRepository<T>, ITransactionalData, IConflictRetrier", "EF Core", "{db}, EfRepository<T>, SqlTransactionalData, EfConflictRetrier", "Azure SQL", "");
var Reset = ("Reset", "IResetSwitch, IQuietCheck", "Reset switch and quiet check", "ConfiguredResetSwitch, ServiceBusQuietCheck", "Service Bus management", "Is a subscription holding messages?");
const string PersonK = "person";

static GraphNode Start(string id, Num col, Num row) => new() { Id = id, Col = col, Row = row, Kind = "planned", W = 110, Title = "Start" };

static GraphNode St(string id, Num col, Num row, string title, string sub = "", string pal = S, int w = 190) =>
    new() { Id = id, Col = col, Row = row, Palette = pal, W = w, Title = title, Sub = sub };

LegendItem[] LegStates = [L("A state", S), L("An end state, success", End), L("An end state, nothing carried out", Off), LegPlanned];
const string FootStates = "Source of truth: the status enums and transition methods in src/Tms/Norvia.Tms.Domain, and GLOSSARY.md. A transition that fails its guard returns false or a refusal and changes nothing.";

// The hexagon of one context. Callers and driving adapters are rows that line up with each other; domain is a list of
// (title, sub), up to six; ports is a list of (port title, port sub, adapter title, adapter sub, outside title, outside sub).
GraphSpec Hexagon(
    string title,
    string subtitle,
    string footnote,
    IReadOnlyList<(string Title, string Sub, string? Kind)> callers,
    IReadOnlyList<(string Title, string Sub)> driving,
    string mediator,
    (string Title, string Sub) usecases,
    IReadOnlyList<(string Title, string Sub)> domain,
    IReadOnlyList<(string Port, string PortSub, string Adapter, string AdapterSub, string Outside, string OutsideSub)> ports,
    string domainTitle = "Domain: aggregates, value objects, specifications")
{
    const double r0 = 0.5;
    List<GraphNode> nodes = [];
    List<GraphEdge> edges = [];
    var n = driving.Count;
    var drows = Enumerable.Range(0, n).Select(i => r0 + (1.15 * i) + 0.0).ToList();
    var total = 0.0;
    foreach (var row in drows)
    {
        total += row;
    }

    var mid = total / n;
    for (var i = 0; i < n; i++)
    {
        var (ct, cs, ck) = callers[i];
        var (dt, ds) = driving[i];
        nodes.Add(new GraphNode { Id = $"c{i}", Col = -1.1, Row = drows[i], Palette = "gray", Kind = ck, W = 250, Title = ct, Sub = cs });
        nodes.Add(new GraphNode { Id = $"w{i}", Col = 0.2, Row = drows[i], Palette = Drive, W = 270, Title = dt, Sub = ds });
        edges.Add(new GraphEdge($"c{i}", $"w{i}"));
        edges.Add(new GraphEdge($"w{i}", "mediator"));
    }

    nodes.Add(new GraphNode { Id = "mediator", Col = 1.5, Row = mid, Palette = Use, W = 240, Title = "Mediator", Sub = mediator });
    nodes.Add(new GraphNode { Id = "uc", Col = 2.7, Row = mid, Palette = Use, W = 300, Title = usecases.Title, Sub = usecases.Sub });
    edges.Add(new GraphEdge("mediator", "uc"));
    List<string> dnames = [];
    for (var i = 0; i < domain.Count; i++)
    {
        var c = 4.1 + (1.1 * (i % 2));
        var r = r0 - 0.1 + (1.1 * (i / 2));
        nodes.Add(new GraphNode { Id = $"d{i}", Col = c, Row = r, Palette = Core, W = 250, Title = domain[i].Title, Sub = domain[i].Sub });
        edges.Add(new GraphEdge("uc", $"d{i}"));
        dnames.Add($"d{i}");
    }

    var prow = domain.Count > 0 ? r0 + (1.15 * ((domain.Count + 1) / 2)) + 0.9 : mid - 1.2;
    List<string> pnames = [];
    List<string> anames = [];
    for (var i = 0; i < ports.Count; i++)
    {
        var (pt, ps, at, asub, ot, os) = ports[i];
        var row = prow + (1.1 * i);
        nodes.Add(new GraphNode { Id = $"p{i}", Col = 4.6, Row = row, Palette = Use, W = 330, Title = pt, Sub = ps });
        nodes.Add(new GraphNode { Id = $"a{i}", Col = 6.5, Row = row, Palette = Adapt, W = 300, Title = at, Sub = asub });
        nodes.Add(new GraphNode { Id = $"o{i}", Col = 7.9, Row = row, Palette = "gray", W = 210, Title = ot, Sub = os });
        edges.Add(new GraphEdge("uc", $"p{i}"));
        edges.Add(new GraphEdge($"a{i}", $"p{i}", "implements"));
        edges.Add(new GraphEdge($"a{i}", $"o{i}"));
        pnames.Add($"p{i}");
        anames.Add($"a{i}");
    }

    List<GraphGroup> groups = [new GraphGroup { Id = "driving", Title = "Driving adapters", Palette = Drive, Members = [.. Enumerable.Range(0, n).Select(i => $"w{i}")] }];
    List<string> appMembers = ["mediator", "uc", .. pnames];
    if (domain.Count > 0)
    {
        groups.Add(new GraphGroup { Id = "domain", Title = domainTitle, Palette = Core, Members = dnames });
        appMembers.Add("domain");
    }

    groups.Add(new GraphGroup { Id = "app", Title = "Application: use cases and ports", Palette = Use, Members = appMembers });
    groups.Add(new GraphGroup { Id = "infra", Title = "Infrastructure: driven adapters", Palette = Adapt, Members = anames });
    return new GraphSpec
    {
        Title = title,
        Subtitle = subtitle,
        Footnote = footnote,
        Cell = (360, 112),
        Nodes = nodes,
        Groups = groups,
        Edges = edges,
        Legend = [L("Driving adapters", Drive), L("Application", Use), L("Domain", Core), L("Infrastructure", Adapt), L("Outside", "gray"), LegPerson],
    };
}

// ------------------------------------------------------------------ context map
specs["context-map"] = new GraphSpec
{
    Title = "Context map",
    Subtitle = "Norvia's bounded contexts: what each owns, what it holds only a copy of, and how they relate. Every relationship runs through the integration layer, never directly.",
    Footnote = "Source of truth: docs/adr (0002, 0005, 0006, 0012 to 0017, 0039) and GLOSSARY.md. Relationship names are Domain-Driven Design's patterns.",
    Legend = [L("Outside Norvia", "gray"), L("TMS", "blue"), L("ERP", "green"), L("Driver app", "teal"), L("Simulator", "orange"), L("Integration console", "pink"), LegPlanned],
    Cell = (540, 150),
    Nodes = [
        new GraphNode { Id = "portal", Col = 0, Row = 1.4, Palette = "gray", W = 300, Title = "Partner portal", Sub = "Owns, for each Persona, its own records of what it sent Norvia and what Norvia answered. Outside Norvia: it plays the Shippers and Carriers" },
        new GraphNode { Id = "wms", Col = 1.5, Row = -0.1, Kind = "planned", W = 300, Title = "WMS", Sub = "Owns: Fulfilment Order, Inventory" },
        new GraphNode { Id = "tms", Col = 1.5, Row = 1.4, Palette = "blue", W = 340, Title = "TMS", Sub = "Owns: Transport Order, Shipment, Carrier booking, Trip, Tracking event, Proof of delivery, Exception, Shipper notification, Own fleet availability, Freight Charge, Carrier cost\nHolds copies of: Shipper, Carrier, Vehicle, Driver, Rate Cards" },
        new GraphNode { Id = "erp", Col = 3.1, Row = 1.4, Palette = "green", W = 340, Title = "ERP", Sub = "Owns: Shipper, Carrier, Vehicle and Driver master data, Contract, Rate Card, Carrier Rate Card, Billable line, Shipper invoice, Credit note, Carrier invoice\nHolds copies of: nothing it did not receive as an event" },
        new GraphNode { Id = "driver", Col = 1.5, Row = 3.3, Palette = "teal", W = 340, Title = "Driver app", Sub = "Owns: the Driver's view of a Trip version, and the reports not yet recorded (Sending, Recorded, Refused, Not recorded). Decides nothing" },
        new GraphNode { Id = "simulator", Col = 1.5, Row = 4.9, Palette = "orange", W = 340, Title = "Simulator", Sub = "Owns: its own runs and logs. Acts only through the estate's real doors" },
        new GraphNode { Id = "console", Col = 3.1, Row = 3.1, Palette = "pink", W = 340, Title = "Integration console", Sub = "Owns nothing: it reads the estate's telemetry and the dead-letter queues" },
    ],
    Groups = [
        new GraphGroup { Id = "norvia", Title = "Norvia Logistics B.V.", Palette = "blue", Members = ["wms", "tms", "erp", "driver", "simulator", "console"] },
    ],
    Edges = [
        new GraphEdge("erp", "tms", "Upstream: master data, Rate Cards (ADR 0006, 0013, 0014). The seed file plays it until E3") { Dashed = true, Sides = ("l", "r") },
        new GraphEdge("tms", "erp", "Upstream: billing facts (ADR 0012). The ERP conforms to the Canonical Model"),
        new GraphEdge("portal", "tms", "Open host service: the Norvia Partner API (ADR 0005)"),
        new GraphEdge("tms", "portal", "Requests and notifications to the Personas' endpoints"),
        new GraphEdge("tms", "driver", "Trips, whole versions (ADR 0015)") { Orient = "v" },
        new GraphEdge("driver", "tms", "Reports, in order per Trip (ADR 0016)") { Orient = "v" },
        new GraphEdge("wms", "tms", "Upstream: Fulfilment Transport Orders (planned)") { Dashed = true, Orient = "v" },
        new GraphEdge("simulator", "portal", "Plays the Personas (ADR 0039)") { Sides = ("l", "b") },
        new GraphEdge("console", "tms", "Reads every context's telemetry (ADR 0017)") { Sides = ("l", "r") },
    ],
};


// ------------------------------------------------------------------ TMS hexagon
specs["tms-hexagon"] = new GraphSpec
{
    Title = "TMS · Clean Architecture as a hexagon",
    Subtitle = "Dependencies point inwards. The Domain knows nothing outside itself. The Application defines the ports it needs, and Infrastructure implements them. Architecture tests enforce this.",
    Footnote = "Source of truth: src/Tms, docs/coding-guidelines.md section 1 and tests/Tms/Norvia.Tms.ArchitectureTests/DependencyRuleTests.cs. Use case counts are the handlers in each Application feature folder.",
    Legend = [L("Driving adapters (Api, Web, workers)", Drive), L("Application", Use), L("Domain", Core), L("Infrastructure", Adapt), L("Outside the TMS", "gray"), LegPerson],
    Cell = (360, 112),
    Nodes = [
        new GraphNode { Id = "callers", Col = -1.1, Row = 1.0, Palette = "gray", W = 250, Title = "Logic Apps, API Management, the Simulator", Sub = "Each with its own identity and app role" },
        new GraphNode { Id = "planner", Col = -1.1, Row = 3.4, Kind = "person", W = 250, Title = "Planner, Customer service", Sub = "Through the Role switcher" },
        new GraphNode { Id = "clock", Col = -1.1, Row = 5.6, Palette = "gray", W = 250, Title = "The clock and signals", Sub = "A timer, a rating signal, a start-up" },
        new GraphNode { Id = "api", Col = 0.2, Row = 1.0, Palette = Drive, W = 270, Title = "TMS API", Sub = "[Minimal API, one endpoint file per feature]\nChecks the app role, translates HTTP to a command or query, returns a problem with a stable code" },
        new GraphNode { Id = "web", Col = 0.2, Row = 3.4, Palette = Drive, W = 270, Title = "TMS UI", Sub = "[Blazor]\nReads in process through queries. Changes things only through the TMS API (ADR 0028)" },
        new GraphNode { Id = "workers", Col = 0.2, Row = 5.6, Palette = Drive, W = 270, Title = "Hosted workers", Sub = "[in Infrastructure]\nRatingWorker, PlanningHorizonWorker, LoadSeedOnStart. They send commands like any caller" },
        new GraphNode { Id = "mediator", Col = 1.5, Row = 3.4, Palette = Use, W = 240, Title = "Mediator", Sub = "Pipeline behaviours add the tracking keys to the logging scope and trigger rating after an acceptance" },
        new GraphNode { Id = "uc", Col = 2.7, Row = 3.4, Palette = Use, W = 300, Title = "Use cases · 80 handlers", Sub = "One command or query and one handler each, by feature:\nOrders and planning 19 · Trips and fleet 28 · Carrier bookings 7 · Tracking, proofs and Shipper reads 10 · Exceptions and notifications 10 · Billing, reset and master data 6" },
        new GraphNode { Id = "d1", Col = 4.1, Row = 0.4, Palette = Core, W = 250, Title = "Transport Orders and Shipments", Sub = "TransportOrder, Shipment, assignments. Rating result, Billing state, Freight Charge" },
        new GraphNode { Id = "d2", Col = 5.2, Row = 0.4, Palette = Core, W = 250, Title = "Carrier bookings", Sub = "CarrierBooking and its overdue rules" },
        new GraphNode { Id = "d3", Col = 4.1, Row = 1.5, Palette = Core, W = 250, Title = "Trips", Sub = "Trip, Stops, the delivery state of each version" },
        new GraphNode { Id = "d4", Col = 5.2, Row = 1.5, Palette = Core, W = 250, Title = "Fleet capacity", Sub = "PlanningDay, Roster, Absence, Driving-hour budget. Vehicle and Driver copies" },
        new GraphNode { Id = "d5", Col = 4.1, Row = 2.6, Palette = Core, W = 250, Title = "Tracking and proofs", Sub = "TrackingEvent, ProofOfDelivery" },
        new GraphNode { Id = "d6", Col = 5.2, Row = 2.6, Palette = Core, W = 250, Title = "Exceptions, notifications, Rate Cards", Sub = "ExceptionCase, ShipperNotification, RateCard, CarrierRateCard" },
        new GraphNode { Id = "p1", Col = 4.6, Row = 4.5, Palette = Use, W = 330, Title = "Persistence", Sub = "IRepository<T>, IReadRepository<T>, ITransactionalData, IConflictRetrier, the number sequences of Transport Orders, Shipments, Carrier bookings, Trips and Exceptions" },
        new GraphNode { Id = "p2", Col = 4.6, Row = 5.6, Palette = Use, W = 330, Title = "Events", Sub = "IOutbox" },
        new GraphNode { Id = "p3", Col = 4.6, Row = 6.7, Palette = Use, W = 330, Title = "Master data copies", Sub = "IShipperDirectory, ICarrierDirectory, IRateCardDirectory, ICarrierRateCardDirectory, IDriverDirectory, IVehicleDirectory, IRosterDirectory" },
        new GraphNode { Id = "p4", Col = 4.6, Row = 7.8, Palette = Use, W = 330, Title = "Proof of delivery files", Sub = "IProofOfDeliveryFiles" },
        new GraphNode { Id = "p5", Col = 4.6, Row = 8.9, Palette = Use, W = 330, Title = "Signals, switches and the clock", Sub = "IRatingTrigger, IResetSwitch, IQuietCheck, IPlannedDrivingHours, TimeProvider" },
        new GraphNode { Id = "a1", Col = 6.5, Row = 4.5, Palette = Adapt, W = 300, Title = "EF Core", Sub = "TmsDbContext, EfRepository<T>, SqlTransactionalData, EfConflictRetrier, Sql…Numbers" },
        new GraphNode { Id = "a2", Col = 6.5, Row = 5.6, Palette = Adapt, W = 300, Title = "Outbox", Sub = "OutboxBuffer saves with the change, OutboxPublisher sends every second, oldest first" },
        new GraphNode { Id = "a3", Col = 6.5, Row = 6.7, Palette = Adapt, W = 300, Title = "Seed-file directories", Sub = "SeedFile…Directory ×7. The ERP's master data until the sync exists" },
        new GraphNode { Id = "a4", Col = 6.5, Row = 7.8, Palette = Adapt, W = 300, Title = "Blob storage", Sub = "AzureBlobProofOfDeliveryFiles" },
        new GraphNode { Id = "a5", Col = 6.5, Row = 8.9, Palette = Adapt, W = 300, Title = "Clock, rating signal, reset", Sub = "FileControlledTimeProvider, RatingSignal, ConfiguredResetSwitch, ServiceBusQuietCheck, TripPlannedDrivingHours" },
        new GraphNode { Id = "o1", Col = 7.9, Row = 4.5, Palette = "gray", W = 210, Title = "Azure SQL" },
        new GraphNode { Id = "o2", Col = 7.9, Row = 5.6, Palette = "gray", W = 210, Title = "Service Bus", Sub = "Topics the Logic Apps subscribe to" },
        new GraphNode { Id = "o3", Col = 7.9, Row = 6.7, Palette = "gray", W = 210, Title = "Seed file" },
        new GraphNode { Id = "o4", Col = 7.9, Row = 7.8, Palette = "gray", W = 210, Title = "Blob storage" },
        new GraphNode { Id = "o5", Col = 7.9, Row = 8.9, Palette = "gray", W = 210, Title = "Service Bus management", Sub = "Is a subscription holding messages?" },
    ],
    Groups = [
        new GraphGroup { Id = "driving", Title = "Driving adapters", Palette = Drive, Members = ["api", "web", "workers"] },
        new GraphGroup { Id = "domain", Title = "Domain: aggregates, value objects, specifications", Palette = Core, Members = ["d1", "d2", "d3", "d4", "d5", "d6"] },
        new GraphGroup { Id = "app", Title = "Application: use cases and ports", Palette = Use, Members = ["mediator", "uc", "domain", "p1", "p2", "p3", "p4", "p5"] },
        new GraphGroup { Id = "infra", Title = "Infrastructure: driven adapters", Palette = Adapt, Members = ["a1", "a2", "a3", "a4", "a5"] },
    ],
    Edges = [
        new GraphEdge("callers", "api", "HTTPS"),
        new GraphEdge("planner", "web", "browser"),
        new GraphEdge("clock", "workers", ""),
        new GraphEdge("api", "mediator", "commands and queries"),
        new GraphEdge("web", "mediator", "queries"),
        new GraphEdge("workers", "mediator", "commands"),
        new GraphEdge("web", "api", "commands over HTTP, as its own identity") { Orient = "v", Sides = ("t", "b") },
        new GraphEdge("mediator", "uc", ""),
        new GraphEdge("uc", "d1", ""),
        new GraphEdge("uc", "d2", ""),
        new GraphEdge("uc", "d3", ""),
        new GraphEdge("uc", "d4", ""),
        new GraphEdge("uc", "d5", ""),
        new GraphEdge("uc", "d6", ""),
        new GraphEdge("uc", "p1", ""),
        new GraphEdge("uc", "p2", ""),
        new GraphEdge("uc", "p3", ""),
        new GraphEdge("uc", "p4", ""),
        new GraphEdge("uc", "p5", ""),
        new GraphEdge("a1", "p1", "implements"),
        new GraphEdge("a2", "p2", "implements"),
        new GraphEdge("a3", "p3", "implements"),
        new GraphEdge("a4", "p4", "implements"),
        new GraphEdge("a5", "p5", "implements"),
        new GraphEdge("a1", "o1", ""),
        new GraphEdge("a2", "o2", ""),
        new GraphEdge("a3", "o3", ""),
        new GraphEdge("a4", "o4", ""),
        new GraphEdge("a5", "o5", ""),
    ],
};


// ------------------------------------------------------------------ the other contexts' hexagons



specs["erp-hexagon"] = Hexagon(
    "ERP · Clean Architecture as a hexagon",
    "The ERP owns master data and invoicing. It takes commands as HTTP, publishes what happened through its outbox, and never uses another application's code.",
    StdFoot.Replace("{app}", "Erp"),
    [
        ("Logic Apps", "Billing, Carrier cost, Shipper invoice, Carrier invoice and its outcome, each with its own identity and role", null),
        ("Finance", "Through the ERP UI: Invoice now, Credit note, Carrier invoice decisions", PersonK),
        ("The clock and start-up", "A monthly timer, a start-up", null),
    ],
    [
        ("ERP API", "[Minimal API]\nChecks the app role, records what an event carries, never touches EF Core"),
        ("ERP UI", "[Blazor]\nReads and changes things in process, through the same commands the endpoints use"),
        ("Hosted workers", "[in Infrastructure]\nMonthlyInvoiceRunWorker, LoadSeedOnStart, OutboxPublisher"),
    ],
    "Pipeline behaviours add the tracking-contract keys to the logging scope for a use case",
    ("Use cases · 30 handlers", "One command or query and one handler each, by feature:\nShipper invoices 6 · Carrier invoices 7 · Credit notes 5 · Master data 5 · Billable lines 3 · Carrier costs 2 · Reset 2"),
    [
        ("Billable lines", "BillableLine: Waiting, then Invoiced exactly once"),
        ("Shipper invoices and Credit notes", "ShipperInvoice, CreditNote: Issued, Sent, Not delivered"),
        ("Carrier costs", "CarrierCost: Open, then Invoiced"),
        ("Carrier invoices", "CarrierInvoice and its lines: matching, Finance's decisions"),
        ("Master data", "Shipper, Carrier, RateCard, CarrierRateCard: read-only, loaded from the seed file"),
    ],
    [
        (Shared.Item1, Shared.Item2, Shared.Item3, Shared.Item4.Replace("{db}", "ErpDbContext"), Shared.Item5, "The ERP's own database"),
        ("Events", "IOutbox", "Outbox", "OutboxBuffer saves with the change, OutboxPublisher sends it", "Service Bus", "The invoicing-events topic"),
        ("Matching tolerance", "IMatchingTolerance", "Matching options", "MatchingOptions: EUR 0 by default", "Configuration", ""),
        Reset,
    ]);


specs["driver-app-hexagon"] = Hexagon(
    "Driver app · Clean Architecture as a hexagon",
    "The Driver app reports and the TMS decides. It makes no HTTP call at all: it is called, and it publishes what the Driver did as events.",
    StdFoot.Replace("{app}", "DriverApp"),
    [
        ("Trip dispatch and Driver reports Logic Apps", "Trip endpoint with the endpoint key, outcome endpoint with a role", null),
        ("Driver", "Signs in with Entra: the Driver ID is their app role", PersonK),
        ("The clock", "A timer", null),
    ],
    [
        ("Driver app API", "[Minimal API]\nTrip endpoint, outcome endpoint, the Driver's own actions"),
        ("Driver app UI", "[Blazor]\nThe Driver's Trips and Stops, with the state of each report"),
        ("Hosted workers", "[in Infrastructure]\nOutboxPublisher"),
    ],
    "Pipeline behaviours add the tracking-contract keys to the logging scope for a use case. Presentation reaches the Application only, never the Domain",
    ("Use cases · 13 handlers", "One command or query and one handler each, by feature:\nReports 5 · Trips 3 · Settings 2 · Reset 2 · Drivers 1"),
    [
        ("Trips", "DriverTrip: the version the Driver app holds, taken or ignored by version"),
        ("Reports and Stops", "DriverReport: Sending, Recorded, Refused, Not recorded. Stop and Trip end rules"),
        ("Proof of delivery form", "The form and its rules at a delivery Stop"),
    ],
    [
        (Shared.Item1, Shared.Item2, Shared.Item3, Shared.Item4.Replace("{db}", "DriverAppDbContext"), Shared.Item5, "The Driver app's own database"),
        ("Events", "IDriverEventOutbox", "Outbox", "OutboxBuffer saves with the report, OutboxPublisher sends it in the Trip's session", "Service Bus", "The driver-app-events topic"),
        ("Proof of delivery in transit", "IProofOfDeliveryTransit", "Blob storage", "AzureBlobProofOfDeliveryTransit", "Transit container", "Files wait here until the TMS has them"),
        ("Driver directory", "IDriverDirectory", "Seed-file directory", "SeedFileDriverDirectory", "Seed file", ""),
        Reset,
    ]);


specs["partner-portal-hexagon"] = Hexagon(
    "Partner portal · Clean Architecture as a hexagon",
    "The Partner portal plays every Shipper and Carrier. Norvia's Logic Apps call its Persona endpoints, and it calls Norvia only through the Norvia Partner API.",
    StdFoot.Replace("{app}", "PartnerPortal"),
    [
        ("Logic Apps", "Booking requests, cancellations, notifications, invoices, each with the Persona's endpoint key", null),
        ("Presenter", "Chooses the Persona, books, answers, tracks", PersonK),
        ("The clock", "Timers for automatic answers and tracking", null),
    ],
    [
        ("Persona endpoints", "[Minimal API, in the Web project]\nWhat Norvia's Logic Apps send to. Acknowledge only"),
        ("Partner portal UI", "[Blazor]\nOne page set per Persona, and the Presenter panel"),
        ("Hosted workers", "[in Infrastructure]\nAutoAnswerWorker, AutoProgressWorker"),
    ],
    "One command or query per use case. No pipeline behaviours yet",
    ("Use cases · 35 handlers", "One command or query and one handler each, by feature:\nCarrier bookings 7 · Carrier shipments 6 · Carrier invoices 5 · Shipper tracking 4 · Bookings 3 · Shipper invoices 2 · Shipper notifications 2 · Presenter faults and sign-ins 3 · Reset 2 · Personas 1"),
    [
        ("Bookings", "A Shipper's drafts and what Norvia answered"),
        ("Carrier bookings", "CarrierBookingRequest: Open, Confirmed, Rejected, Withdrawn or Cancelled by Norvia"),
        ("Carrier invoices", "A Carrier's invoices and Norvia's outcome"),
        ("Shipper invoices and notifications", "What Norvia sent each Shipper Persona"),
        ("Personas", "One fictional party each, with its own identity"),
    ],
    [
        (Shared.Item1, Shared.Item2, Shared.Item3, Shared.Item4.Replace("{db}", "PartnerPortalDbContext"), Shared.Item5, "The portal's own database"),
        ("Norvia Partner API", "INorviaPartnerApi", "Partner API client", "NorviaPartnerApiClient with the Persona's token, Entra in Azure, a local issuer under Aspire", "API Management", "The only way in to Norvia"),
        ("Persona and Driver directories", "IPersonaDirectory, IDriverSignInDirectory", "Seed-file directories", "SeedFilePersonaDirectory, SeedFileDriverSignInDirectory", "Seed file", ""),
        ("Driver app settings", "IDriverAppSettings", "Driver app client", "DriverAppSettingsClient", "Driver app", "The Presenter panel's fault switch"),
        Reset,
    ]);


specs["simulator-hexagon"] = Hexagon(
    "Simulator · Clean Architecture as a hexagon",
    "The Simulator is a caller of the estate. It acts only through the doors a real party would use, and never writes to another database or publishes to Service Bus itself.",
    StdFoot.Replace("{app}", "Simulator"),
    [
        ("Presenter", "Chooses a Preset, previews the plan, starts a run", PersonK),
        ("The clock", "Paces a run, resumes nothing after a restart", null),
    ],
    [
        ("Simulator UI", "[Blazor]\nConfigure, Plan preview, Runs, Log. There is no API"),
        ("Hosted worker", "[in Infrastructure]\nRunExecutor: carries out the one active run"),
    ],
    "One command or query per use case. No pipeline behaviours yet",
    ("Use cases · 12 handlers", "One command or query and one handler each, by feature:\nRuns 7 · Reset 2 · Configure 1 · Log 1 · Plans 1"),
    [
        ("Worlds and Plans", "The seeded Shippers, Carriers and Drivers, and a Plan from the settings and a seed"),
        ("Runs and the log", "SimulationRun: Running, then Completed, Cancelled or Failed. What each actor did"),
        ("Actors", "What a Shipper, Carrier, Planner, Driver and Finance do, and when"),
    ],
    [
        (Shared.Item1, Shared.Item2, Shared.Item3, Shared.Item4.Replace("{db}", "SimulatorDbContext"), Shared.Item5, "The Simulator's own runs and logs"),
        ("Norvia Partner API", "IPartnerApi", "Partner API client", "NorviaPartnerApiClient as each Persona", "API Management", "Books, answers, tracks, sends Proofs"),
        ("TMS API and ERP API", "ITmsApi, IErpApi", "API clients", "NorviaTmsApiClient, NorviaErpApiClient as the Simulator's own identity", "TMS API, ERP API", "Plans, releases, Invoice now"),
        ("Driver app and inboxes", "IDriverApp, IShipperInbox, ICarrierInbox", "Clients", "NorviaDriverAppClient as each Driver, the Partner portal's inbox clients", "Driver app, Partner portal", "Works the Trips, reads invoices"),
        ("Estate reset", "IEstateReset", "Reset client", "NorviaEstateResetClient: each application's own reset endpoint", "Every application", ""),
        ("The world and the seed", "IWorldSource, ISeedSource", "Seed sources", "SeedFileWorldSource, RandomSeedSource", "Seed file", "The same seed always gives the same plan"),
    ]);


specs["integration-console-hexagon"] = Hexagon(
    "Integration console · Clean Architecture as a hexagon",
    "The console owns nothing, so it has no Domain and no database. It reads the estate's telemetry and the dead-letter queues, and changes only Service Bus.",
    StdFoot.Replace("{app}", "IntegrationConsole"),
    [("Integration operator", "Signs in with Easy Auth and needs a console role", PersonK)],
    [("Integration console UI", "[Blazor]\nSearch, the Message trail, failed runs, dead-letter queues. There is no API")],
    "One command or query per use case. No pipeline behaviours yet",
    ("Use cases · 12 handlers", "One command or query and one handler each, by feature:\nDead letters 7 · Runs 3 · Trails 2"),
    [],
    [
        ("Message trails", "ITrailSource", "Trail sources", "LogAnalyticsTrailSource in Azure, DashboardTrailSource under Aspire", "Application Insights, Log Analytics", "The Aspire dashboard locally"),
        ("Failed runs and run details", "IFailedRuns, IWorkflowRuns", "Logic Apps clients", "LogicAppsWorkflowRuns in Azure, DashboardTrailSource under Aspire", "Logic Apps management API", "Run history and step bodies"),
        ("Dead-letter queues", "IDeadLetterQueues", "Service Bus", "ServiceBusDeadLetterQueues, ServiceBusClients", "Service Bus", "Peeks, resubmits to one subscription, discards"),
        ("Audit", "IAuditLog", "Telemetry", "OpenTelemetryAuditLog", "Application Insights", "Every resubmit and discard, with who and why"),
    ]);


// ------------------------------------------------------------------ TMS lifecycles

specs["tms-state-transport-order"] = new GraphSpec
{
    Title = "TMS · Transport Order lifecycle",
    Subtitle = "Three separate dimensions on one aggregate: where the order stands (status), what rating made of it, and whether it can be billed. A rating failure never rejects an order.",
    Footnote = FootStates,
    Legend = LegStates,
    Cell = (360, 110),
    Nodes = [
        Start("s0", 0, 0.0),
        St("accepted", 1.2, 0.0, "Accepted", "Waiting for planning"),
        St("planned", 2.4, 0.0, "Planned", "A Shipment exists", End),
        St("rejected", 1.2, 1.0, "Rejected", "Kept as a Rejected booking with every reason", Off, 230),
        Start("r0", 0, 2.6),
        St("notyet", 1.2, 2.6, "Not yet rated", "A Fulfilment order starts Not rated separately"),
        St("rated", 2.4, 2.6, "Rated", "Freight Charge frozen", End),
        St("failed", 1.2, 3.7, "Rating failed", "A stable Rating failure code", "orange"),
        St("nrs", 3.6, 2.6, "Not rated separately", "A Fulfilment order, billed through its Contract", Off, 230),
        Start("b0", 0, 5.2),
        St("nyb", 1.2, 5.2, "Not yet billable"),
        St("billable", 2.4, 5.2, "Billable", "Delivered, Rated, with a Proof of delivery, no open Exception. Handed to the ERP once, never reverted", End, 260),
        St("nbs", 3.6, 5.2, "Not billed separately", "A Fulfilment order", Off, 230),
    ],
    Groups = [
        new GraphGroup { Id = "g1", Title = "Status (TransportOrderStatus)", Palette = S, Members = ["s0", "accepted", "planned", "rejected"] },
        new GraphGroup { Id = "g2", Title = "Rating result (P2)", Palette = "blue", Members = ["r0", "notyet", "rated", "failed", "nrs"] },
        new GraphGroup { Id = "g3", Title = "Billing state (P9)", Palette = "green", Members = ["b0", "nyb", "billable", "nbs"] },
    ],
    Edges = [
        new GraphEdge("s0", "accepted", "Submit: no rule broken"),
        new GraphEdge("accepted", "planned", "Plan: the Planner assigns it"),
        new GraphEdge("s0", "rejected", "Submit: it breaks a rule") { Orient = "v", Sides = ("b", "l") },
        new GraphEdge("r0", "notyet", "Accept"),
        new GraphEdge("notyet", "rated", "Rate: the Rate Card prices it"),
        new GraphEdge("notyet", "failed", "Rate: no price found") { Orient = "v" },
        new GraphEdge("failed", "rated", "Re-rate after the Rate Card is corrected") { Sides = ("r", "b") },
        new GraphEdge("b0", "nyb", "Accept"),
        new GraphEdge("nyb", "billable", "TryBecomeBillable"),
    ],
};


specs["tms-state-shipment"] = new GraphSpec
{
    Title = "TMS · Shipment lifecycle",
    Subtitle = "Own fleet and Carrier Shipments share one status. Tracking events move a Shipment forward only, and never past what they report. Awaiting re-delivery is a flag, not a status.",
    Footnote = FootStates,
    Legend = LegStates,
    Cell = (300, 120),
    Nodes = [
        Start("s0", 0, 1.2),
        St("planned", 1.1, 1.2, "Planned", "Assigned to Own fleet or a Carrier"),
        St("released", 2.3, 1.2, "Released", "Handed over to be carried out"),
        St("booked", 3.8, 0.2, "Booked", "Carrier only: its booking is Confirmed"),
        St("dispatched", 3.8, 2.2, "Dispatched", "Own fleet only: its Trip is dispatched"),
        St("collected", 5.1, 1.2, "Collected"),
        St("transit", 6.3, 1.2, "In transit"),
        St("delivered", 7.5, 1.2, "Delivered", "", End),
        new GraphNode { Id = "await", Col = 5.7, Row = 3.0, Kind = "planned", W = 260, Title = "Awaiting re-delivery", Sub = "A flag on an Own fleet Shipment, between its Trips" },
    ],
    Groups = [],
    Edges = [
        new GraphEdge("s0", "planned", "Plan: the Planner assigns the Transport Order"),
        new GraphEdge("planned", "released", "Release"),
        new GraphEdge("released", "booked", "Book: the Carrier confirmed") { Sides = ("r", "l") },
        new GraphEdge("released", "dispatched", "Dispatch: its Trip is dispatched") { Sides = ("r", "l") },
        new GraphEdge("booked", "collected", "Tracking event: collected"),
        new GraphEdge("dispatched", "collected", "Tracking event: collected"),
        new GraphEdge("collected", "transit", "Tracking event: in transit"),
        new GraphEdge("transit", "delivered", "Tracking event: delivered"),
        new GraphEdge("collected", "delivered", "A later event may skip ahead") { Via = "top", Sides = ("t", "t") },
        new GraphEdge("released", "planned", "ReturnToPlanned: the Carrier booking ended, or TakeOffTrip") { Via = "bottom", Sides = ("b", "b") },
        new GraphEdge("booked", "planned", "ReturnToPlannedAfterCancellation: Cancel") { Via = "top", Sides = ("t", "t") },
        new GraphEdge("dispatched", "planned", "TakeOffTrip: before it is collected") { Via = "bottom", Sides = ("b", "b") },
        new GraphEdge("collected", "await", "AwaitRedelivery: its Trip ended without delivering it") { Dashed = true, Orient = "v" },
        new GraphEdge("await", "dispatched", "Dispatch on a later Trip clears the flag") { Dashed = true },
    ],
};


specs["tms-state-carrier-booking"] = new GraphSpec
{
    Title = "TMS · Carrier booking lifecycle",
    Subtitle = "A Shipment can have several Carrier bookings over its life, but only one open at a time. Every end except Confirmed puts the Shipment back to Planned.",
    Footnote = FootStates + " Overdue is derived from the time a booking has been Requested; it is not a status.",
    Legend = LegStates,
    Cell = (330, 120),
    Nodes = [
        Start("s0", 0, 1.2),
        St("requested", 1.1, 1.2, "Requested", "Sent to the Carrier, no answer yet"),
        St("confirmed", 2.6, 0.2, "Confirmed", "The Shipment is Booked, the Carrier cost frozen", End, 230),
        St("rejected", 2.6, 1.2, "Rejected", "The Carrier's reason code is kept", Off),
        St("notdelivered", 2.6, 2.2, "Not delivered", "The send failed, with the status code", Off),
        St("withdrawn", 2.6, 3.2, "Withdrawn", "The Planner took it back before an answer", Off),
        St("cancelled", 4.1, 0.2, "Cancelled", "The Planner cancelled it before collection", Off),
    ],
    Groups = [],
    Edges = [
        new GraphEdge("s0", "requested", "Release of a Carrier Shipment"),
        new GraphEdge("requested", "confirmed", "Confirm: the Carrier answers yes"),
        new GraphEdge("requested", "rejected", "Reject: the Carrier answers no"),
        new GraphEdge("requested", "notdelivered", "ReportNotDelivered: the Logic App gave up"),
        new GraphEdge("requested", "withdrawn", "Withdraw"),
        new GraphEdge("confirmed", "cancelled", "Cancel: to resolve an Exception"),
    ],
};


specs["tms-state-trip"] = new GraphSpec
{
    Title = "TMS · Trip lifecycle",
    Subtitle = "A Trip's status follows the Planner and then the Driver. Each version the Planner sends has its own delivery state, so a Trip the Driver app never received is visible.",
    Footnote = FootStates,
    Legend = LegStates,
    Cell = (320, 120),
    Nodes = [
        Start("s0", 0, 0.0),
        St("draft", 1.1, 0.0, "Draft", "Holds its Vehicle and Driver, nothing is published"),
        St("dispatched", 2.4, 0.0, "Dispatched", "Sent as version 1. Amend sends a new version of the whole Trip, and it stays Dispatched", S, 230),
        St("progress", 3.7, 0.0, "In progress", "Only an uncollected Shipment can be taken off"),
        St("completed", 5.0, 0.0, "Completed", "", End),
        St("discarded", 1.1, 1.1, "Discarded", "Deleted: only a Draft can be", Off),
        Start("v0", 0, 3.0),
        St("sending", 1.1, 3.0, "Sending", "Published, no answer yet"),
        St("delivered", 2.4, 3.0, "Delivered", "The Driver app took the version", End),
        St("notdel", 1.1, 4.6, "Not delivered", "The Driver app refused it, or the Logic App gave up", Off, 230),
    ],
    Groups = [
        new GraphGroup { Id = "g1", Title = "Trip status (TripStatus)", Palette = S, Members = ["s0", "draft", "dispatched", "progress", "completed", "discarded"] },
        new GraphGroup { Id = "g2", Title = "Delivery state of a version (TripDeliveryState)", Palette = "teal", Members = ["v0", "sending", "delivered", "notdel"] },
    ],
    Edges = [
        new GraphEdge("s0", "draft", "CreateDraftTrip"),
        new GraphEdge("draft", "dispatched", "Dispatch: Vehicle, Driver, Planned start and Shipments all present"),
        new GraphEdge("dispatched", "progress", "Start: the Driver, on the Trip's day"),
        new GraphEdge("progress", "completed", "End: every Stop has an outcome"),
        new GraphEdge("draft", "discarded", "Discard") { Orient = "v" },
        new GraphEdge("v0", "sending", "A version is created"),
        new GraphEdge("sending", "delivered", "ReportDelivered"),
        new GraphEdge("sending", "notdel", "ReportNotDelivered") { Orient = "v" },
        new GraphEdge("notdel", "sending", "SendAgain: the Planner") { Orient = "v" },
    ],
};


specs["tms-state-supporting"] = new GraphSpec
{
    Title = "TMS · Smaller lifecycles",
    Subtitle = "The Exception, the Shipper notification and the planning day: three smaller aggregates with two or three states each.",
    Footnote = FootStates + " The TMS never closes an Exception: the Planner does, and a Cause cleared marker does not change its status.",
    Legend = LegStates,
    Cell = (330, 120),
    Nodes = [
        Start("e0", 0, 0),
        St("open", 1.1, 0, "Open", "Cause marker may say it has cleared"),
        St("resolved", 2.2, 0, "Resolved", "With the Planner's outcome", End),
        Start("n0", 0, 1.8),
        St("req", 1.1, 1.8, "Requested", "A Shipper notification, not yet reported"),
        St("sent", 2.2, 1.8, "Sent", "", End),
        St("nd", 2.2, 2.8, "Not delivered", "With the technical cause", Off),
        Start("d0", 0, 4.6),
        St("dopen", 1.1, 4.6, "Open", "The Planner can still change it"),
        St("closed", 2.2, 4.6, "Closed", "Before today: read-only", Off),
    ],
    Groups = [
        new GraphGroup { Id = "g1", Title = "Exception (ExceptionStatus)", Palette = S, Members = ["e0", "open", "resolved"] },
        new GraphGroup { Id = "g2", Title = "Shipper notification (ShipperNotificationStatus)", Palette = "blue", Members = ["n0", "req", "sent", "nd"] },
        new GraphGroup { Id = "g3", Title = "Planning day (PlanningDayStatus)", Palette = "blue", Members = ["d0", "dopen", "closed"] },
    ],
    Edges = [
        new GraphEdge("e0", "open", "A Tracking event or Proof of delivery, or Late delivery, opens it"),
        new GraphEdge("open", "resolved", "Resolve"),
        new GraphEdge("n0", "req", "NotifyShipper"),
        new GraphEdge("req", "sent", "ReportSent"),
        new GraphEdge("req", "nd", "ReportNotDelivered"),
        new GraphEdge("d0", "dopen", "The daily horizon job opens the day"),
        new GraphEdge("dopen", "closed", "The daily horizon job closes days before today"),
    ],
};



// ------------------------------------------------------------------ ERP, Driver app, Partner portal and Simulator lifecycles
specs["erp-state-invoicing"] = new GraphSpec
{
    Title = "ERP · Billable line, invoice and Carrier cost lifecycles",
    Subtitle = "The ERP records what the TMS hands it, invoices it exactly once, and keeps Carrier costs open until a Carrier invoice line settles them.",
    Footnote = FootStates.Replace("src/Tms/Norvia.Tms.Domain", "src/Erp/Norvia.Erp.Domain"),
    Legend = LegStates,
    Cell = (340, 120),
    Nodes = [
        Start("b0", 0, 0.0),
        St("waiting", 1.1, 0.0, "Waiting", "Recorded once from TransportOrderBillable"),
        St("invoiced", 2.3, 0.0, "Invoiced", "Holds its Shipper invoice number, never invoiced again", End, 250),
        Start("i0", 0, 1.9),
        St("issued", 1.1, 1.9, "Issued", "A Shipper invoice or a Credit note"),
        St("sent", 2.3, 1.9, "Sent", "The Persona's endpoint acknowledged it", End),
        St("nd", 2.3, 3.0, "Not delivered", "Finance can send it again", Off),
        Start("c0", 0, 4.8),
        St("open", 1.1, 4.8, "Open", "Recorded once from CarrierCostIncurred"),
        St("cinv", 2.3, 4.8, "Invoiced", "A Carrier invoice line settled it", End),
    ],
    Groups = [
        new GraphGroup { Id = "g1", Title = "Billable line (BillableLineStatus)", Palette = "green", Members = ["b0", "waiting", "invoiced"] },
        new GraphGroup { Id = "g2", Title = "Shipper invoice and Credit note (ShipperInvoiceStatus)", Palette = "green", Members = ["i0", "issued", "sent", "nd"] },
        new GraphGroup { Id = "g3", Title = "Carrier cost (CarrierCostStatus)", Palette = "green", Members = ["c0", "open", "cinv"] },
    ],
    Edges = [
        new GraphEdge("b0", "waiting", "RecordBillableLine"),
        new GraphEdge("waiting", "invoiced", "Invoice: an Invoice run, once"),
        new GraphEdge("i0", "issued", "IssueShipperInvoice, IssueCreditNote"),
        new GraphEdge("issued", "sent", "ReportSent"),
        new GraphEdge("issued", "nd", "ReportNotDelivered") { Orient = "v" },
        new GraphEdge("nd", "issued", "SendAgain") { Orient = "v" },
        new GraphEdge("c0", "open", "RecordCarrierCost"),
        new GraphEdge("open", "cinv", "MarkInvoiced"),
    ],
};


specs["erp-state-carrier-invoice"] = new GraphSpec
{
    Title = "ERP · Carrier invoice matching lifecycle",
    Subtitle = "Each line is matched against the Carrier cost of its Carrier booking. The invoice is Approved once no line is left for Finance to decide.",
    Footnote = FootStates.Replace("src/Tms/Norvia.Tms.Domain", "src/Erp/Norvia.Erp.Domain"),
    Legend = LegStates,
    Cell = (340, 120),
    Nodes = [
        Start("s0", 0, 0.5),
        St("received", 1.1, 0.0, "Received", "At least one line to decide"),
        St("approved", 2.4, 0.5, "Approved", "The Carrier is told the outcome of each line", End, 230),
        Start("l0", 0, 2.6),
        St("matched", 1.1, 2.0, "Matched", "Within the Matching tolerance of the Carrier cost", End, 230),
        St("todecide", 1.1, 3.4, "To decide", "Price mismatch, unknown booking or duplicate"),
        St("accepted", 2.4, 2.6, "Accepted", "At the invoiced amount, or adjusted with a remark", End, 230),
        St("rejected", 2.4, 3.9, "Rejected", "With a reason", Off),
    ],
    Groups = [
        new GraphGroup { Id = "g1", Title = "Carrier invoice (CarrierInvoiceStatus)", Palette = "green", Members = ["s0", "received", "approved"] },
        new GraphGroup { Id = "g2", Title = "Carrier invoice line (CarrierInvoiceLineStatus)", Palette = "green", Members = ["l0", "matched", "todecide", "accepted", "rejected"] },
    ],
    Edges = [
        new GraphEdge("s0", "received", "ReceiveCarrierInvoice"),
        new GraphEdge("s0", "approved", "Receive: every line Matched") { Via = "top", Sides = ("t", "t") },
        new GraphEdge("received", "approved", "A decision leaves no line to decide"),
        new GraphEdge("l0", "matched", "Match: within the tolerance"),
        new GraphEdge("l0", "todecide", "Match: it does not"),
        new GraphEdge("todecide", "accepted", "Accept, or MatchAgain when its Carrier cost arrives"),
        new GraphEdge("todecide", "rejected", "Reject"),
    ],
};


specs["driver-app-state-report"] = new GraphSpec
{
    Title = "Driver app · Driver report lifecycle",
    Subtitle = "A report is saved and shown as Sending until the Driver reports Logic App says what became of it. Trip progress is derived from the reports, never stored.",
    Footnote = FootStates.Replace("src/Tms/Norvia.Tms.Domain", "src/DriverApp/Norvia.DriverApp.Domain"),
    Legend = LegStates,
    Cell = (340, 120),
    Nodes = [
        Start("r0", 0, 0.0),
        St("sending", 1.1, 0.0, "Sending", "Published through the outbox, no answer yet"),
        St("recorded", 2.4, -0.55, "Recorded", "The TMS accepted it", End),
        St("refused", 2.4, 0.45, "Refused", "With the TMS's failure code and message", Off),
        St("notrec", 2.4, 1.45, "Not recorded", "The Logic App gave up after its retries", Off),
        Start("p0", 0, 3.4),
        St("held", 1.1, 3.4, "Version held", "A Trip version from the TMS, a lower one is ignored"),
        St("started", 2.4, 3.4, "Started", "A Start Trip report is Recorded"),
        St("ended", 3.7, 3.4, "Ended", "An End Trip report is Recorded", End),
    ],
    Groups = [
        new GraphGroup { Id = "g1", Title = "Report state (ReportState)", Palette = "teal", Members = ["r0", "sending", "recorded", "refused", "notrec"] },
        new GraphGroup { Id = "g2", Title = "Trip progress (derived by TripProgress)", Palette = "teal", Members = ["p0", "held", "started", "ended"] },
    ],
    Edges = [
        new GraphEdge("r0", "sending", "The Driver reports"),
        new GraphEdge("sending", "recorded", "ReportOutcome: Recorded"),
        new GraphEdge("sending", "refused", "ReportOutcome: Refused"),
        new GraphEdge("sending", "notrec", "ReportOutcome: Not recorded"),
        new GraphEdge("p0", "held", "ReceiveTripVersion"),
        new GraphEdge("held", "started", "StartTrip"),
        new GraphEdge("started", "ended", "EndTrip: every Stop has an outcome"),
    ],
};


specs["partner-portal-state-carrier-booking-request"] = new GraphSpec
{
    Title = "Partner portal · Carrier booking request lifecycle",
    Subtitle = "What a Carrier Persona sees of a booking request from Norvia, kept as the Carrier's own record. It mirrors Norvia's Carrier booking, one step behind.",
    Footnote = FootStates.Replace("src/Tms/Norvia.Tms.Domain", "src/PartnerPortal/Norvia.PartnerPortal.Domain"),
    Legend = LegStates,
    Cell = (340, 120),
    Nodes = [
        Start("s0", 0, 1.1),
        St("open", 1.1, 1.1, "Open", "Waiting for the Persona's answer"),
        St("confirmed", 2.4, 0.0, "Confirmed", "The Persona answered yes", End),
        St("rejected", 2.4, 1.1, "Rejected", "With the Persona's reason code", Off),
        St("withdrawn", 2.4, 2.2, "Withdrawn by Norvia", "Norvia took it back before an answer", Off, 230),
        St("cancelled", 3.7, 0.0, "Cancelled by Norvia", "Norvia cancelled it before collection", Off, 230),
    ],
    Groups = [],
    Edges = [
        new GraphEdge("s0", "open", "ReceiveCarrierBooking"),
        new GraphEdge("open", "confirmed", "Answer: confirm"),
        new GraphEdge("open", "rejected", "Answer: reject"),
        new GraphEdge("open", "withdrawn", "WithdrawnByNorvia"),
        new GraphEdge("confirmed", "cancelled", "CancelledByNorvia"),
    ],
};


specs["simulator-state-run"] = new GraphSpec
{
    Title = "Simulator · Simulation run lifecycle",
    Subtitle = "Only one run is active at a time. A run can be cancelled but not resumed after a restart, and its log stays after a refresh.",
    Footnote = FootStates.Replace("src/Tms/Norvia.Tms.Domain", "src/Simulator/Norvia.Simulator.Domain"),
    Legend = LegStates,
    Cell = (340, 120),
    Nodes = [
        Start("s0", 0, 1.1),
        St("running", 1.1, 1.1, "Running", "Sending its plan, in a burst or paced"),
        St("completed", 2.4, 0.0, "Completed", "The whole plan was carried out", End),
        St("cancelled", 2.4, 1.1, "Cancelled", "The Presenter cancelled it", Off),
        St("failed", 2.4, 2.2, "Failed", "Still running when the Simulator restarted", "orange"),
    ],
    Groups = [],
    Edges = [
        new GraphEdge("s0", "running", "StartRun"),
        new GraphEdge("running", "completed", "Complete"),
        new GraphEdge("running", "cancelled", "Cancel"),
        new GraphEdge("running", "failed", "Fail: AbandonActiveRuns"),
    ],
};

DiagramOutput.RenderAll(specs, DiagramOutput.SpecDirectory(), "", args, GraphDiagram.Render);
