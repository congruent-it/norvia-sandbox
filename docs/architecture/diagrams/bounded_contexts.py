#!/usr/bin/env python3
"""The bounded-context diagrams: the context map, the TMS hexagon and the TMS lifecycles.

    python3 bounded_contexts.py [name ...]    # writes <name>.excalidraw and .png; no names draws them all

Graph style (see README.md). The PNGs need rsvg-convert.
"""
import sys
from pathlib import Path

from excalidraw_graph import render_all
from excalidraw_matrix import PALETTE, PERSON, PLANNED

OUT = Path(__file__).parent
L = lambda label, key: (label, PALETTE[key][2], PALETTE[key][3], False)
LEG_PERSON = ("A person", *PERSON, False)
LEG_PLANNED = ("Planned, not built yet", *PLANNED, True)
SPECS = {}

# ------------------------------------------------------------------ context map
SPECS["context-map"] = dict(
    title="Context map",
    subtitle="Norvia's bounded contexts: what each owns, what it holds only a copy of, and how they relate. "
             "Every relationship runs through the integration layer, never directly.",
    footnote="Source of truth: docs/adr (0002, 0005, 0006, 0012 to 0017, 0039) and GLOSSARY.md. Relationship names are Domain-Driven Design's patterns.",
    legend=[L("Outside Norvia", "gray"), L("TMS", "blue"), L("ERP", "green"), L("Driver app", "teal"), L("Simulator", "orange"), L("Integration console", "pink"), LEG_PLANNED],
    cell=(540, 150),
    nodes=[
        dict(id="portal", col=0, row=1.4, palette="gray", w=300, title="Partner portal",
             sub="Owns, for each Persona, its own records of what it sent Norvia and what Norvia answered. Outside Norvia: it plays the Shippers and Carriers"),
        dict(id="wms", col=1.5, row=-0.1, kind="planned", w=300, title="WMS", sub="Owns: Fulfilment Order, Inventory"),
        dict(id="tms", col=1.5, row=1.4, palette="blue", w=340, title="TMS",
             sub="Owns: Transport Order, Shipment, Carrier booking, Trip, Tracking event, Proof of delivery, Exception, Shipper notification, Own fleet availability, Freight Charge, Carrier cost\nHolds copies of: Shipper, Carrier, Vehicle, Driver, Rate Cards"),
        dict(id="erp", col=3.1, row=1.4, palette="green", w=340, title="ERP",
             sub="Owns: Shipper, Carrier, Vehicle and Driver master data, Contract, Rate Card, Carrier Rate Card, Billable line, Shipper invoice, Credit note, Carrier invoice\nHolds copies of: nothing it did not receive as an event"),
        dict(id="driver", col=1.5, row=3.3, palette="teal", w=340, title="Driver app",
             sub="Owns: the Driver's view of a Trip version, and the reports not yet recorded (Sending, Recorded, Refused, Not recorded). Decides nothing"),
        dict(id="simulator", col=1.5, row=4.9, palette="orange", w=340, title="Simulator", sub="Owns: its own runs and logs. Acts only through the estate's real doors"),
        dict(id="console", col=3.1, row=3.1, palette="pink", w=340, title="Integration console", sub="Owns nothing: it reads the estate's telemetry and the dead-letter queues"),
    ],
    groups=[
        dict(id="norvia", title="Norvia Logistics B.V.", palette="blue", members=["wms", "tms", "erp", "driver", "simulator", "console"]),
    ],
    edges=[
        ("erp", "tms", "Upstream: master data, Rate Cards (ADR 0006, 0013, 0014). The seed file plays it until E3", dict(dashed=True, sides=("l", "r"))),
        ("tms", "erp", "Upstream: billing facts (ADR 0012). The ERP conforms to the Canonical Model"),
        ("portal", "tms", "Open host service: the Norvia Partner API (ADR 0005)"),
        ("tms", "portal", "Requests and notifications to the Personas' endpoints"),
        ("tms", "driver", "Trips, whole versions (ADR 0015)", dict(orient="v")),
        ("driver", "tms", "Reports, in order per Trip (ADR 0016)", dict(orient="v")),
        ("wms", "tms", "Upstream: Fulfilment Transport Orders (planned)", dict(dashed=True, orient="v")),
        ("simulator", "portal", "Plays the Personas (ADR 0039)", dict(sides=("l", "b"))),
        ("console", "tms", "Reads every context's telemetry (ADR 0017)", dict(sides=("l", "r"))),
    ],
)

# ------------------------------------------------------------------ TMS hexagon
CORE, USE, ADAPT, DRIVE = "blue", "teal", "violet", "orange"
SPECS["tms-hexagon"] = dict(
    title="TMS · Clean Architecture as a hexagon",
    subtitle="Dependencies point inwards. The Domain knows nothing outside itself. The Application defines the ports it needs, and "
             "Infrastructure implements them. Architecture tests enforce this.",
    footnote="Source of truth: src/Tms, docs/coding-guidelines.md section 1 and tests/Tms/Norvia.Tms.ArchitectureTests/DependencyRuleTests.cs. "
             "Use case counts are the handlers in each Application feature folder.",
    legend=[L("Driving adapters (Api, Web, workers)", DRIVE), L("Application", USE), L("Domain", CORE), L("Infrastructure", ADAPT), L("Outside the TMS", "gray"), LEG_PERSON],
    cell=(360, 112),
    nodes=[
        dict(id="callers", col=-1.1, row=1.0, palette="gray", w=250, title="Logic Apps, API Management, the Simulator", sub="Each with its own identity and app role"),
        dict(id="planner", col=-1.1, row=3.4, kind="person", w=250, title="Planner, Customer service", sub="Through the Role switcher"),
        dict(id="clock", col=-1.1, row=5.6, palette="gray", w=250, title="The clock and signals", sub="A timer, a rating signal, a start-up"),
        dict(id="api", col=0.2, row=1.0, palette=DRIVE, w=270, title="TMS API", sub="[Minimal API, one endpoint file per feature]\nChecks the app role, translates HTTP to a command or query, returns a problem with a stable code"),
        dict(id="web", col=0.2, row=3.4, palette=DRIVE, w=270, title="TMS UI", sub="[Blazor]\nReads in process through queries. Changes things only through the TMS API (ADR 0028)"),
        dict(id="workers", col=0.2, row=5.6, palette=DRIVE, w=270, title="Hosted workers", sub="[in Infrastructure]\nRatingWorker, PlanningHorizonWorker, LoadSeedOnStart. They send commands like any caller"),
        dict(id="mediator", col=1.5, row=3.4, palette=USE, w=240, title="Mediator", sub="Pipeline behaviours add the tracking keys to the logging scope and trigger rating after an acceptance"),
        dict(id="uc", col=2.7, row=3.4, palette=USE, w=300, title="Use cases · 80 handlers",
             sub="One command or query and one handler each, by feature:\nOrders and planning 19 · Trips and fleet 28 · Carrier bookings 7 · Tracking, proofs and Shipper reads 10 · Exceptions and notifications 10 · Billing, reset and master data 6"),
        dict(id="d1", col=4.1, row=0.4, palette=CORE, w=250, title="Transport Orders and Shipments", sub="TransportOrder, Shipment, assignments. Rating result, Billing state, Freight Charge"),
        dict(id="d2", col=5.2, row=0.4, palette=CORE, w=250, title="Carrier bookings", sub="CarrierBooking and its overdue rules"),
        dict(id="d3", col=4.1, row=1.5, palette=CORE, w=250, title="Trips", sub="Trip, Stops, the delivery state of each version"),
        dict(id="d4", col=5.2, row=1.5, palette=CORE, w=250, title="Fleet capacity", sub="PlanningDay, Roster, Absence, Driving-hour budget. Vehicle and Driver copies"),
        dict(id="d5", col=4.1, row=2.6, palette=CORE, w=250, title="Tracking and proofs", sub="TrackingEvent, ProofOfDelivery"),
        dict(id="d6", col=5.2, row=2.6, palette=CORE, w=250, title="Exceptions, notifications, Rate Cards", sub="ExceptionCase, ShipperNotification, RateCard, CarrierRateCard"),
        dict(id="p1", col=4.6, row=4.5, palette=USE, w=330, title="Persistence", sub="IRepository<T>, IReadRepository<T>, ITransactionalData, IConflictRetrier, the number sequences of Transport Orders, Shipments, Carrier bookings, Trips and Exceptions"),
        dict(id="p2", col=4.6, row=5.6, palette=USE, w=330, title="Events", sub="IOutbox"),
        dict(id="p3", col=4.6, row=6.7, palette=USE, w=330, title="Master data copies", sub="IShipperDirectory, ICarrierDirectory, IRateCardDirectory, ICarrierRateCardDirectory, IDriverDirectory, IVehicleDirectory, IRosterDirectory"),
        dict(id="p4", col=4.6, row=7.8, palette=USE, w=330, title="Proof of delivery files", sub="IProofOfDeliveryFiles"),
        dict(id="p5", col=4.6, row=8.9, palette=USE, w=330, title="Signals, switches and the clock", sub="IRatingTrigger, IResetSwitch, IQuietCheck, IPlannedDrivingHours, TimeProvider"),
        dict(id="a1", col=6.5, row=4.5, palette=ADAPT, w=300, title="EF Core", sub="TmsDbContext, EfRepository<T>, SqlTransactionalData, EfConflictRetrier, Sql…Numbers"),
        dict(id="a2", col=6.5, row=5.6, palette=ADAPT, w=300, title="Outbox", sub="OutboxBuffer saves with the change, OutboxPublisher sends every second, oldest first"),
        dict(id="a3", col=6.5, row=6.7, palette=ADAPT, w=300, title="Seed-file directories", sub="SeedFile…Directory ×7. The ERP's master data until the sync exists"),
        dict(id="a4", col=6.5, row=7.8, palette=ADAPT, w=300, title="Blob storage", sub="AzureBlobProofOfDeliveryFiles"),
        dict(id="a5", col=6.5, row=8.9, palette=ADAPT, w=300, title="Clock, rating signal, reset", sub="FileControlledTimeProvider, RatingSignal, ConfiguredResetSwitch, ServiceBusQuietCheck, TripPlannedDrivingHours"),
        dict(id="o1", col=7.9, row=4.5, palette="gray", w=210, title="Azure SQL"),
        dict(id="o2", col=7.9, row=5.6, palette="gray", w=210, title="Service Bus", sub="Topics the Logic Apps subscribe to"),
        dict(id="o3", col=7.9, row=6.7, palette="gray", w=210, title="Seed file"),
        dict(id="o4", col=7.9, row=7.8, palette="gray", w=210, title="Blob storage"),
        dict(id="o5", col=7.9, row=8.9, palette="gray", w=210, title="Service Bus management", sub="Is a subscription holding messages?"),
    ],
    groups=[
        dict(id="driving", title="Driving adapters", palette=DRIVE, members=["api", "web", "workers"]),
        dict(id="domain", title="Domain: aggregates, value objects, specifications", palette=CORE, members=["d1", "d2", "d3", "d4", "d5", "d6"]),
        dict(id="app", title="Application: use cases and ports", palette=USE, members=["mediator", "uc", "domain", "p1", "p2", "p3", "p4", "p5"]),
        dict(id="infra", title="Infrastructure: driven adapters", palette=ADAPT, members=["a1", "a2", "a3", "a4", "a5"]),
    ],
    edges=[
        ("callers", "api", "HTTPS"), ("planner", "web", "browser"), ("clock", "workers", ""),
        ("api", "mediator", "commands and queries"), ("web", "mediator", "queries"), ("workers", "mediator", "commands"),
        ("web", "api", "commands over HTTP, as its own identity", dict(orient="v", sides=("t", "b"))),
        ("mediator", "uc", ""),
        ("uc", "d1", ""), ("uc", "d2", ""), ("uc", "d3", ""), ("uc", "d4", ""), ("uc", "d5", ""), ("uc", "d6", ""),
        ("uc", "p1", ""), ("uc", "p2", ""), ("uc", "p3", ""), ("uc", "p4", ""), ("uc", "p5", ""),
        ("a1", "p1", "implements"), ("a2", "p2", "implements"), ("a3", "p3", "implements"), ("a4", "p4", "implements"), ("a5", "p5", "implements"),
        ("a1", "o1", ""), ("a2", "o2", ""), ("a3", "o3", ""), ("a4", "o4", ""), ("a5", "o5", ""),
    ],
)

# ------------------------------------------------------------------ the other contexts' hexagons
def hexagon(title, subtitle, footnote, callers, driving, mediator, usecases, domain, ports, domain_title="Domain: aggregates, value objects, specifications"):
    """The hexagon of one context. callers and driving are (title, sub, kind) rows that line up with each other;
    domain is a list of (title, sub), up to six; ports is a list of (port title, port sub, adapter title, adapter sub, outside title, outside sub)."""
    r0 = 0.5
    nodes, edges = [], []
    n = len(driving)
    drows = [r0 + 1.15 * i + 0.0 for i in range(n)]
    mid = sum(drows) / n
    for i, ((ct, cs, ck), (dt, ds)) in enumerate(zip(callers, driving)):
        row = drows[i]
        nodes.append(dict(id=f"c{i}", col=-1.1, row=row, palette="gray", kind=ck, w=250, title=ct, sub=cs))
        nodes.append(dict(id=f"w{i}", col=0.2, row=row, palette=DRIVE, w=270, title=dt, sub=ds))
        edges.append((f"c{i}", f"w{i}", ""))
        edges.append((f"w{i}", "mediator", ""))
    nodes.append(dict(id="mediator", col=1.5, row=mid, palette=USE, w=240, title="Mediator", sub=mediator))
    nodes.append(dict(id="uc", col=2.7, row=mid, palette=USE, w=300, title=usecases[0], sub=usecases[1]))
    edges.append(("mediator", "uc", ""))
    dnames = []
    for i, (dt, ds) in enumerate(domain):
        c, r = 4.1 + 1.1 * (i % 2), r0 - 0.1 + 1.1 * (i // 2)
        nodes.append(dict(id=f"d{i}", col=c, row=r, palette=CORE, w=250, title=dt, sub=ds))
        edges.append(("uc", f"d{i}", ""))
        dnames.append(f"d{i}")
    prow = r0 + 1.15 * ((len(domain) + 1) // 2) + 0.9 if domain else mid - 1.2
    pnames, anames = [], []
    for i, (pt, ps, at, as_, ot, os_) in enumerate(ports):
        row = prow + 1.1 * i
        nodes.append(dict(id=f"p{i}", col=4.6, row=row, palette=USE, w=330, title=pt, sub=ps))
        nodes.append(dict(id=f"a{i}", col=6.5, row=row, palette=ADAPT, w=300, title=at, sub=as_))
        nodes.append(dict(id=f"o{i}", col=7.9, row=row, palette="gray", w=210, title=ot, sub=os_))
        edges += [("uc", f"p{i}", ""), (f"a{i}", f"p{i}", "implements"), (f"a{i}", f"o{i}", "")]
        pnames.append(f"p{i}"); anames.append(f"a{i}")
    groups = [dict(id="driving", title="Driving adapters", palette=DRIVE, members=[f"w{i}" for i in range(n)])]
    app_members = ["mediator", "uc"] + pnames
    if domain:
        groups.append(dict(id="domain", title=domain_title, palette=CORE, members=dnames))
        app_members.append("domain")
    groups.append(dict(id="app", title="Application: use cases and ports", palette=USE, members=app_members))
    groups.append(dict(id="infra", title="Infrastructure: driven adapters", palette=ADAPT, members=anames))
    return dict(title=title, subtitle=subtitle, footnote=footnote, cell=(360, 112), nodes=nodes, groups=groups, edges=edges,
                legend=[L("Driving adapters", DRIVE), L("Application", USE), L("Domain", CORE), L("Infrastructure", ADAPT), L("Outside", "gray"), LEG_PERSON])


STD_FOOT = "Source of truth: src/{app}, docs/coding-guidelines.md section 1 and the architecture tests in tests/{app}. Use case counts are the handlers in each Application feature folder."
SHARED = ("Persistence", "IRepository<T>, IReadRepository<T>, ITransactionalData, IConflictRetrier", "EF Core", "{db}, EfRepository<T>, SqlTransactionalData, EfConflictRetrier", "Azure SQL", "")
RESET = ("Reset", "IResetSwitch, IQuietCheck", "Reset switch and quiet check", "ConfiguredResetSwitch, ServiceBusQuietCheck", "Service Bus management", "Is a subscription holding messages?")
PERSON_K = "person"

SPECS["erp-hexagon"] = hexagon(
    "ERP · Clean Architecture as a hexagon",
    "The ERP owns master data and invoicing. It takes commands as HTTP, publishes what happened through its outbox, and never uses another application's code.",
    STD_FOOT.format(app="Erp"),
    [("Logic Apps", "Billing, Carrier cost, Shipper invoice, Carrier invoice and its outcome, each with its own identity and role", None),
     ("Finance", "Through the ERP UI: Invoice now, Credit note, Carrier invoice decisions", PERSON_K),
     ("The clock and start-up", "A monthly timer, a start-up", None)],
    [("ERP API", "[Minimal API]\nChecks the app role, records what an event carries, never touches EF Core"),
     ("ERP UI", "[Blazor]\nReads and changes things in process, through the same commands the endpoints use"),
     ("Hosted workers", "[in Infrastructure]\nMonthlyInvoiceRunWorker, LoadSeedOnStart, OutboxPublisher")],
    "Pipeline behaviours add the tracking-contract keys to the logging scope for a use case",
    ("Use cases · 30 handlers", "One command or query and one handler each, by feature:\nShipper invoices 6 · Carrier invoices 7 · Credit notes 5 · Master data 5 · Billable lines 3 · Carrier costs 2 · Reset 2"),
    [("Billable lines", "BillableLine: Waiting, then Invoiced exactly once"),
     ("Shipper invoices and Credit notes", "ShipperInvoice, CreditNote: Issued, Sent, Not delivered"),
     ("Carrier costs", "CarrierCost: Open, then Invoiced"),
     ("Carrier invoices", "CarrierInvoice and its lines: matching, Finance's decisions"),
     ("Master data", "Shipper, Carrier, RateCard, CarrierRateCard: read-only, loaded from the seed file")],
    [(SHARED[0], SHARED[1], SHARED[2], SHARED[3].format(db="ErpDbContext"), SHARED[4], "The ERP's own database"),
     ("Events", "IOutbox", "Outbox", "OutboxBuffer saves with the change, OutboxPublisher sends it", "Service Bus", "The invoicing-events topic"),
     ("Matching tolerance", "IMatchingTolerance", "Matching options", "MatchingOptions: EUR 0 by default", "Configuration", ""),
     RESET])

SPECS["driver-app-hexagon"] = hexagon(
    "Driver app · Clean Architecture as a hexagon",
    "The Driver app reports and the TMS decides. It makes no HTTP call at all: it is called, and it publishes what the Driver did as events.",
    STD_FOOT.format(app="DriverApp"),
    [("Trip dispatch and Driver reports Logic Apps", "Trip endpoint with the endpoint key, outcome endpoint with a role", None),
     ("Driver", "Signs in with Entra: the Driver ID is their app role", PERSON_K),
     ("The clock", "A timer", None)],
    [("Driver app API", "[Minimal API]\nTrip endpoint, outcome endpoint, the Driver's own actions"),
     ("Driver app UI", "[Blazor]\nThe Driver's Trips and Stops, with the state of each report"),
     ("Hosted workers", "[in Infrastructure]\nOutboxPublisher")],
    "Pipeline behaviours add the tracking-contract keys to the logging scope for a use case. Presentation reaches the Application only, never the Domain",
    ("Use cases · 13 handlers", "One command or query and one handler each, by feature:\nReports 5 · Trips 3 · Settings 2 · Reset 2 · Drivers 1"),
    [("Trips", "DriverTrip: the version the Driver app holds, taken or ignored by version"),
     ("Reports and Stops", "DriverReport: Sending, Recorded, Refused, Not recorded. Stop and Trip end rules"),
     ("Proof of delivery form", "The form and its rules at a delivery Stop")],
    [(SHARED[0], SHARED[1], SHARED[2], SHARED[3].format(db="DriverAppDbContext"), SHARED[4], "The Driver app's own database"),
     ("Events", "IDriverEventOutbox", "Outbox", "OutboxBuffer saves with the report, OutboxPublisher sends it in the Trip's session", "Service Bus", "The driver-app-events topic"),
     ("Proof of delivery in transit", "IProofOfDeliveryTransit", "Blob storage", "AzureBlobProofOfDeliveryTransit", "Transit container", "Files wait here until the TMS has them"),
     ("Driver directory", "IDriverDirectory", "Seed-file directory", "SeedFileDriverDirectory", "Seed file", ""),
     RESET])

SPECS["partner-portal-hexagon"] = hexagon(
    "Partner portal · Clean Architecture as a hexagon",
    "The Partner portal plays every Shipper and Carrier. Norvia's Logic Apps call its Persona endpoints, and it calls Norvia only through the Norvia Partner API.",
    STD_FOOT.format(app="PartnerPortal"),
    [("Logic Apps", "Booking requests, cancellations, notifications, invoices, each with the Persona's endpoint key", None),
     ("Presenter", "Chooses the Persona, books, answers, tracks", PERSON_K),
     ("The clock", "Timers for automatic answers and tracking", None)],
    [("Persona endpoints", "[Minimal API, in the Web project]\nWhat Norvia's Logic Apps send to. Acknowledge only"),
     ("Partner portal UI", "[Blazor]\nOne page set per Persona, and the Presenter panel"),
     ("Hosted workers", "[in Infrastructure]\nAutoAnswerWorker, AutoProgressWorker")],
    "One command or query per use case. No pipeline behaviours yet",
    ("Use cases · 35 handlers", "One command or query and one handler each, by feature:\nCarrier bookings 7 · Carrier shipments 6 · Carrier invoices 5 · Shipper tracking 4 · Bookings 3 · Shipper invoices 2 · Shipper notifications 2 · Presenter faults and sign-ins 3 · Reset 2 · Personas 1"),
    [("Bookings", "A Shipper's drafts and what Norvia answered"),
     ("Carrier bookings", "CarrierBookingRequest: Open, Confirmed, Rejected, Withdrawn or Cancelled by Norvia"),
     ("Carrier invoices", "A Carrier's invoices and Norvia's outcome"),
     ("Shipper invoices and notifications", "What Norvia sent each Shipper Persona"),
     ("Personas", "One fictional party each, with its own identity")],
    [(SHARED[0], SHARED[1], SHARED[2], SHARED[3].format(db="PartnerPortalDbContext"), SHARED[4], "The portal's own database"),
     ("Norvia Partner API", "INorviaPartnerApi", "Partner API client", "NorviaPartnerApiClient with the Persona's token, Entra in Azure, a local issuer under Aspire", "API Management", "The only way in to Norvia"),
     ("Persona and Driver directories", "IPersonaDirectory, IDriverSignInDirectory", "Seed-file directories", "SeedFilePersonaDirectory, SeedFileDriverSignInDirectory", "Seed file", ""),
     ("Driver app settings", "IDriverAppSettings", "Driver app client", "DriverAppSettingsClient", "Driver app", "The Presenter panel's fault switch"),
     RESET])

SPECS["simulator-hexagon"] = hexagon(
    "Simulator · Clean Architecture as a hexagon",
    "The Simulator is a caller of the estate. It acts only through the doors a real party would use, and never writes to another database or publishes to Service Bus itself.",
    STD_FOOT.format(app="Simulator"),
    [("Presenter", "Chooses a Preset, previews the plan, starts a run", PERSON_K),
     ("The clock", "Paces a run, resumes nothing after a restart", None)],
    [("Simulator UI", "[Blazor]\nConfigure, Plan preview, Runs, Log. There is no API"),
     ("Hosted worker", "[in Infrastructure]\nRunExecutor: carries out the one active run")],
    "One command or query per use case. No pipeline behaviours yet",
    ("Use cases · 12 handlers", "One command or query and one handler each, by feature:\nRuns 7 · Reset 2 · Configure 1 · Log 1 · Plans 1"),
    [("Worlds and Plans", "The seeded Shippers, Carriers and Drivers, and a Plan from the settings and a seed"),
     ("Runs and the log", "SimulationRun: Running, then Completed, Cancelled or Failed. What each actor did"),
     ("Actors", "What a Shipper, Carrier, Planner, Driver and Finance do, and when")],
    [(SHARED[0], SHARED[1], SHARED[2], SHARED[3].format(db="SimulatorDbContext"), SHARED[4], "The Simulator's own runs and logs"),
     ("Norvia Partner API", "IPartnerApi", "Partner API client", "NorviaPartnerApiClient as each Persona", "API Management", "Books, answers, tracks, sends Proofs"),
     ("TMS API and ERP API", "ITmsApi, IErpApi", "API clients", "NorviaTmsApiClient, NorviaErpApiClient as the Simulator's own identity", "TMS API, ERP API", "Plans, releases, Invoice now"),
     ("Driver app and inboxes", "IDriverApp, IShipperInbox, ICarrierInbox", "Clients", "NorviaDriverAppClient as each Driver, the Partner portal's inbox clients", "Driver app, Partner portal", "Works the Trips, reads invoices"),
     ("Estate reset", "IEstateReset", "Reset client", "NorviaEstateResetClient: each application's own reset endpoint", "Every application", ""),
     ("The world and the seed", "IWorldSource, ISeedSource", "Seed sources", "SeedFileWorldSource, RandomSeedSource", "Seed file", "The same seed always gives the same plan")])

SPECS["integration-console-hexagon"] = hexagon(
    "Integration console · Clean Architecture as a hexagon",
    "The console owns nothing, so it has no Domain and no database. It reads the estate's telemetry and the dead-letter queues, and changes only Service Bus.",
    STD_FOOT.format(app="IntegrationConsole"),
    [("Integration operator", "Signs in with Easy Auth and needs a console role", PERSON_K)],
    [("Integration console UI", "[Blazor]\nSearch, the Message trail, failed runs, dead-letter queues. There is no API")],
    "One command or query per use case. No pipeline behaviours yet",
    ("Use cases · 12 handlers", "One command or query and one handler each, by feature:\nDead letters 7 · Runs 3 · Trails 2"),
    [],
    [("Message trails", "ITrailSource", "Trail sources", "LogAnalyticsTrailSource in Azure, DashboardTrailSource under Aspire", "Application Insights, Log Analytics", "The Aspire dashboard locally"),
     ("Failed runs and run details", "IFailedRuns, IWorkflowRuns", "Logic Apps clients", "LogicAppsWorkflowRuns in Azure, DashboardTrailSource under Aspire", "Logic Apps management API", "Run history and step bodies"),
     ("Dead-letter queues", "IDeadLetterQueues", "Service Bus", "ServiceBusDeadLetterQueues, ServiceBusClients", "Service Bus", "Peeks, resubmits to one subscription, discards"),
     ("Audit", "IAuditLog", "Telemetry", "OpenTelemetryAuditLog", "Application Insights", "Every resubmit and discard, with who and why")])

# ------------------------------------------------------------------ TMS lifecycles
S, END, OFF = "blue", "green", "gray"
start = lambda i, c, r: dict(id=i, col=c, row=r, kind="planned", w=110, title="Start")
st = lambda i, c, r, title, sub="", pal=S, w=190: dict(id=i, col=c, row=r, palette=pal, w=w, title=title, sub=sub)
LEG_STATES = [L("A state", S), L("An end state, success", END), L("An end state, nothing carried out", OFF), LEG_PLANNED]
FOOT_STATES = "Source of truth: the status enums and transition methods in src/Tms/Norvia.Tms.Domain, and GLOSSARY.md. A transition that fails its guard returns false or a refusal and changes nothing."

SPECS["tms-state-transport-order"] = dict(
    title="TMS · Transport Order lifecycle",
    subtitle="Three separate dimensions on one aggregate: where the order stands (status), what rating made of it, and whether it can be billed. "
             "A rating failure never rejects an order.",
    footnote=FOOT_STATES, legend=LEG_STATES, cell=(360, 110),
    nodes=[
        start("s0", 0, 0.0), st("accepted", 1.2, 0.0, "Accepted", "Waiting for planning"), st("planned", 2.4, 0.0, "Planned", "A Shipment exists", END),
        st("rejected", 1.2, 1.0, "Rejected", "Kept as a Rejected booking with every reason", OFF, 230),
        start("r0", 0, 2.6), st("notyet", 1.2, 2.6, "Not yet rated", "A Fulfilment order starts Not rated separately"), st("rated", 2.4, 2.6, "Rated", "Freight Charge frozen", END), st("failed", 1.2, 3.7, "Rating failed", "A stable Rating failure code", "orange"),
        st("nrs", 3.6, 2.6, "Not rated separately", "A Fulfilment order, billed through its Contract", OFF, 230),
        start("b0", 0, 5.2), st("nyb", 1.2, 5.2, "Not yet billable"), st("billable", 2.4, 5.2, "Billable", "Delivered, Rated, with a Proof of delivery, no open Exception. Handed to the ERP once, never reverted", END, 260),
        st("nbs", 3.6, 5.2, "Not billed separately", "A Fulfilment order", OFF, 230),
    ],
    groups=[
        dict(id="g1", title="Status (TransportOrderStatus)", palette=S, members=["s0", "accepted", "planned", "rejected"]),
        dict(id="g2", title="Rating result (P2)", palette="blue", members=["r0", "notyet", "rated", "failed", "nrs"]),
        dict(id="g3", title="Billing state (P9)", palette="green", members=["b0", "nyb", "billable", "nbs"]),
    ],
    edges=[
        ("s0", "accepted", "Submit: no rule broken"), ("accepted", "planned", "Plan: the Planner assigns it"),
        ("s0", "rejected", "Submit: it breaks a rule", dict(orient="v", sides=("b", "l"))),
        ("r0", "notyet", "Accept"), ("notyet", "rated", "Rate: the Rate Card prices it"),
        ("notyet", "failed", "Rate: no price found", dict(orient="v")),
        ("failed", "rated", "Re-rate after the Rate Card is corrected", dict(sides=("r", "b"))),
        ("b0", "nyb", "Accept"), ("nyb", "billable", "TryBecomeBillable"),
    ],
)

SPECS["tms-state-shipment"] = dict(
    title="TMS · Shipment lifecycle",
    subtitle="Own fleet and Carrier Shipments share one status. Tracking events move a Shipment forward only, and never past what they report. "
             "Awaiting re-delivery is a flag, not a status.",
    footnote=FOOT_STATES, legend=LEG_STATES, cell=(300, 120),
    nodes=[
        start("s0", 0, 1.2), st("planned", 1.1, 1.2, "Planned", "Assigned to Own fleet or a Carrier"), st("released", 2.3, 1.2, "Released", "Handed over to be carried out"),
        st("booked", 3.8, 0.2, "Booked", "Carrier only: its booking is Confirmed"), st("dispatched", 3.8, 2.2, "Dispatched", "Own fleet only: its Trip is dispatched"),
        st("collected", 5.1, 1.2, "Collected"), st("transit", 6.3, 1.2, "In transit"), st("delivered", 7.5, 1.2, "Delivered", "", END),
        dict(id="await", col=5.7, row=3.0, kind="planned", w=260, title="Awaiting re-delivery", sub="A flag on an Own fleet Shipment, between its Trips"),
    ],
    groups=[],
    edges=[
        ("s0", "planned", "Plan: the Planner assigns the Transport Order"), ("planned", "released", "Release"),
        ("released", "booked", "Book: the Carrier confirmed", dict(sides=("r", "l"))), ("released", "dispatched", "Dispatch: its Trip is dispatched", dict(sides=("r", "l"))),
        ("booked", "collected", "Tracking event: collected"), ("dispatched", "collected", "Tracking event: collected"),
        ("collected", "transit", "Tracking event: in transit"), ("transit", "delivered", "Tracking event: delivered"),
        ("collected", "delivered", "A later event may skip ahead", dict(via="top", sides=("t", "t"))),
        ("released", "planned", "ReturnToPlanned: the Carrier booking ended, or TakeOffTrip", dict(via="bottom", sides=("b", "b"))),
        ("booked", "planned", "ReturnToPlannedAfterCancellation: Cancel", dict(via="top", sides=("t", "t"))),
        ("dispatched", "planned", "TakeOffTrip: before it is collected", dict(via="bottom", sides=("b", "b"))),
        ("collected", "await", "AwaitRedelivery: its Trip ended without delivering it", dict(dashed=True, orient="v")),
        ("await", "dispatched", "Dispatch on a later Trip clears the flag", dict(dashed=True)),
    ],
)

SPECS["tms-state-carrier-booking"] = dict(
    title="TMS · Carrier booking lifecycle",
    subtitle="A Shipment can have several Carrier bookings over its life, but only one open at a time. Every end except Confirmed puts the Shipment back to Planned.",
    footnote=FOOT_STATES + " Overdue is derived from the time a booking has been Requested; it is not a status.",
    legend=LEG_STATES, cell=(330, 120),
    nodes=[
        start("s0", 0, 1.2), st("requested", 1.1, 1.2, "Requested", "Sent to the Carrier, no answer yet"),
        st("confirmed", 2.6, 0.2, "Confirmed", "The Shipment is Booked, the Carrier cost frozen", END, 230),
        st("rejected", 2.6, 1.2, "Rejected", "The Carrier's reason code is kept", OFF),
        st("notdelivered", 2.6, 2.2, "Not delivered", "The send failed, with the status code", OFF),
        st("withdrawn", 2.6, 3.2, "Withdrawn", "The Planner took it back before an answer", OFF),
        st("cancelled", 4.1, 0.2, "Cancelled", "The Planner cancelled it before collection", OFF),
    ],
    groups=[],
    edges=[
        ("s0", "requested", "Release of a Carrier Shipment"),
        ("requested", "confirmed", "Confirm: the Carrier answers yes"), ("requested", "rejected", "Reject: the Carrier answers no"),
        ("requested", "notdelivered", "ReportNotDelivered: the Logic App gave up"), ("requested", "withdrawn", "Withdraw"),
        ("confirmed", "cancelled", "Cancel: to resolve an Exception"),
    ],
)

SPECS["tms-state-trip"] = dict(
    title="TMS · Trip lifecycle",
    subtitle="A Trip's status follows the Planner and then the Driver. Each version the Planner sends has its own delivery state, so a Trip the Driver app never received is visible.",
    footnote=FOOT_STATES, legend=LEG_STATES, cell=(320, 120),
    nodes=[
        start("s0", 0, 0.0), st("draft", 1.1, 0.0, "Draft", "Holds its Vehicle and Driver, nothing is published"), st("dispatched", 2.4, 0.0, "Dispatched", "Sent as version 1. Amend sends a new version of the whole Trip, and it stays Dispatched", S, 230),
        st("progress", 3.7, 0.0, "In progress", "Only an uncollected Shipment can be taken off"), st("completed", 5.0, 0.0, "Completed", "", END),
        st("discarded", 1.1, 1.1, "Discarded", "Deleted: only a Draft can be", OFF),
        start("v0", 0, 3.0), st("sending", 1.1, 3.0, "Sending", "Published, no answer yet"), st("delivered", 2.4, 3.0, "Delivered", "The Driver app took the version", END), st("notdel", 1.1, 4.6, "Not delivered", "The Driver app refused it, or the Logic App gave up", OFF, 230),
    ],
    groups=[
        dict(id="g1", title="Trip status (TripStatus)", palette=S, members=["s0", "draft", "dispatched", "progress", "completed", "discarded"]),
        dict(id="g2", title="Delivery state of a version (TripDeliveryState)", palette="teal", members=["v0", "sending", "delivered", "notdel"]),
    ],
    edges=[
        ("s0", "draft", "CreateDraftTrip"), ("draft", "dispatched", "Dispatch: Vehicle, Driver, Planned start and Shipments all present"),
        ("dispatched", "progress", "Start: the Driver, on the Trip's day"), ("progress", "completed", "End: every Stop has an outcome"),
        ("draft", "discarded", "Discard", dict(orient="v")),
        ("v0", "sending", "A version is created"), ("sending", "delivered", "ReportDelivered"), ("sending", "notdel", "ReportNotDelivered", dict(orient="v")),
        ("notdel", "sending", "SendAgain: the Planner", dict(orient="v")),
    ],
)

SPECS["tms-state-supporting"] = dict(
    title="TMS · Smaller lifecycles",
    subtitle="The Exception, the Shipper notification and the planning day: three smaller aggregates with two or three states each.",
    footnote=FOOT_STATES + " The TMS never closes an Exception: the Planner does, and a Cause cleared marker does not change its status.",
    legend=LEG_STATES, cell=(330, 120),
    nodes=[
        start("e0", 0, 0), st("open", 1.1, 0, "Open", "Cause marker may say it has cleared"), st("resolved", 2.2, 0, "Resolved", "With the Planner's outcome", END),
        start("n0", 0, 1.8), st("req", 1.1, 1.8, "Requested", "A Shipper notification, not yet reported"), st("sent", 2.2, 1.8, "Sent", "", END), st("nd", 2.2, 2.8, "Not delivered", "With the technical cause", OFF),
        start("d0", 0, 4.6), st("dopen", 1.1, 4.6, "Open", "The Planner can still change it"), st("closed", 2.2, 4.6, "Closed", "Before today: read-only", OFF),
    ],
    groups=[
        dict(id="g1", title="Exception (ExceptionStatus)", palette=S, members=["e0", "open", "resolved"]),
        dict(id="g2", title="Shipper notification (ShipperNotificationStatus)", palette="blue", members=["n0", "req", "sent", "nd"]),
        dict(id="g3", title="Planning day (PlanningDayStatus)", palette="blue", members=["d0", "dopen", "closed"]),
    ],
    edges=[
        ("e0", "open", "A Tracking event or Proof of delivery, or Late delivery, opens it"), ("open", "resolved", "Resolve"),
        ("n0", "req", "NotifyShipper"), ("req", "sent", "ReportSent"), ("req", "nd", "ReportNotDelivered"),
        ("d0", "dopen", "The daily horizon job opens the day"), ("dopen", "closed", "The daily horizon job closes days before today"),
    ],
)


# ------------------------------------------------------------------ ERP, Driver app, Partner portal and Simulator lifecycles
SPECS["erp-state-invoicing"] = dict(
    title="ERP · Billable line, invoice and Carrier cost lifecycles",
    subtitle="The ERP records what the TMS hands it, invoices it exactly once, and keeps Carrier costs open until a Carrier invoice line settles them.",
    footnote=FOOT_STATES.replace("src/Tms/Norvia.Tms.Domain", "src/Erp/Norvia.Erp.Domain"), legend=LEG_STATES, cell=(340, 120),
    nodes=[
        start("b0", 0, 0.0), st("waiting", 1.1, 0.0, "Waiting", "Recorded once from TransportOrderBillable"), st("invoiced", 2.3, 0.0, "Invoiced", "Holds its Shipper invoice number, never invoiced again", END, 250),
        start("i0", 0, 1.9), st("issued", 1.1, 1.9, "Issued", "A Shipper invoice or a Credit note"), st("sent", 2.3, 1.9, "Sent", "The Persona's endpoint acknowledged it", END), st("nd", 2.3, 3.0, "Not delivered", "Finance can send it again", OFF),
        start("c0", 0, 4.8), st("open", 1.1, 4.8, "Open", "Recorded once from CarrierCostIncurred"), st("cinv", 2.3, 4.8, "Invoiced", "A Carrier invoice line settled it", END),
    ],
    groups=[
        dict(id="g1", title="Billable line (BillableLineStatus)", palette="green", members=["b0", "waiting", "invoiced"]),
        dict(id="g2", title="Shipper invoice and Credit note (ShipperInvoiceStatus)", palette="green", members=["i0", "issued", "sent", "nd"]),
        dict(id="g3", title="Carrier cost (CarrierCostStatus)", palette="green", members=["c0", "open", "cinv"]),
    ],
    edges=[
        ("b0", "waiting", "RecordBillableLine"), ("waiting", "invoiced", "Invoice: an Invoice run, once"),
        ("i0", "issued", "IssueShipperInvoice, IssueCreditNote"), ("issued", "sent", "ReportSent"), ("issued", "nd", "ReportNotDelivered", dict(orient="v")),
        ("nd", "issued", "SendAgain", dict(orient="v")),
        ("c0", "open", "RecordCarrierCost"), ("open", "cinv", "MarkInvoiced"),
    ],
)

SPECS["erp-state-carrier-invoice"] = dict(
    title="ERP · Carrier invoice matching lifecycle",
    subtitle="Each line is matched against the Carrier cost of its Carrier booking. The invoice is Approved once no line is left for Finance to decide.",
    footnote=FOOT_STATES.replace("src/Tms/Norvia.Tms.Domain", "src/Erp/Norvia.Erp.Domain"), legend=LEG_STATES, cell=(340, 120),
    nodes=[
        start("s0", 0, 0.5), st("received", 1.1, 0.0, "Received", "At least one line to decide"), st("approved", 2.4, 0.5, "Approved", "The Carrier is told the outcome of each line", END, 230),
        start("l0", 0, 2.6), st("matched", 1.1, 2.0, "Matched", "Within the Matching tolerance of the Carrier cost", END, 230), st("todecide", 1.1, 3.4, "To decide", "Price mismatch, unknown booking or duplicate"),
        st("accepted", 2.4, 2.6, "Accepted", "At the invoiced amount, or adjusted with a remark", END, 230), st("rejected", 2.4, 3.9, "Rejected", "With a reason", OFF),
    ],
    groups=[
        dict(id="g1", title="Carrier invoice (CarrierInvoiceStatus)", palette="green", members=["s0", "received", "approved"]),
        dict(id="g2", title="Carrier invoice line (CarrierInvoiceLineStatus)", palette="green", members=["l0", "matched", "todecide", "accepted", "rejected"]),
    ],
    edges=[
        ("s0", "received", "ReceiveCarrierInvoice"), ("s0", "approved", "Receive: every line Matched", dict(via="top", sides=("t", "t"))),
        ("received", "approved", "A decision leaves no line to decide"),
        ("l0", "matched", "Match: within the tolerance"), ("l0", "todecide", "Match: it does not"),
        ("todecide", "accepted", "Accept, or MatchAgain when its Carrier cost arrives"), ("todecide", "rejected", "Reject"),
    ],
)

SPECS["driver-app-state-report"] = dict(
    title="Driver app · Driver report lifecycle",
    subtitle="A report is saved and shown as Sending until the Driver reports Logic App says what became of it. Trip progress is derived from the reports, never stored.",
    footnote=FOOT_STATES.replace("src/Tms/Norvia.Tms.Domain", "src/DriverApp/Norvia.DriverApp.Domain"), legend=LEG_STATES, cell=(340, 120),
    nodes=[
        start("r0", 0, 0.0), st("sending", 1.1, 0.0, "Sending", "Published through the outbox, no answer yet"),
        st("recorded", 2.4, -0.55, "Recorded", "The TMS accepted it", END), st("refused", 2.4, 0.45, "Refused", "With the TMS's failure code and message", OFF), st("notrec", 2.4, 1.45, "Not recorded", "The Logic App gave up after its retries", OFF),
        start("p0", 0, 3.4), st("held", 1.1, 3.4, "Version held", "A Trip version from the TMS, a lower one is ignored"), st("started", 2.4, 3.4, "Started", "A Start Trip report is Recorded"), st("ended", 3.7, 3.4, "Ended", "An End Trip report is Recorded", END),
    ],
    groups=[
        dict(id="g1", title="Report state (ReportState)", palette="teal", members=["r0", "sending", "recorded", "refused", "notrec"]),
        dict(id="g2", title="Trip progress (derived by TripProgress)", palette="teal", members=["p0", "held", "started", "ended"]),
    ],
    edges=[
        ("r0", "sending", "The Driver reports"), ("sending", "recorded", "ReportOutcome: Recorded"), ("sending", "refused", "ReportOutcome: Refused"), ("sending", "notrec", "ReportOutcome: Not recorded"),
        ("p0", "held", "ReceiveTripVersion"), ("held", "started", "StartTrip"), ("started", "ended", "EndTrip: every Stop has an outcome"),
    ],
)

SPECS["partner-portal-state-carrier-booking-request"] = dict(
    title="Partner portal · Carrier booking request lifecycle",
    subtitle="What a Carrier Persona sees of a booking request from Norvia, kept as the Carrier's own record. It mirrors Norvia's Carrier booking, one step behind.",
    footnote=FOOT_STATES.replace("src/Tms/Norvia.Tms.Domain", "src/PartnerPortal/Norvia.PartnerPortal.Domain"), legend=LEG_STATES, cell=(340, 120),
    nodes=[
        start("s0", 0, 1.1), st("open", 1.1, 1.1, "Open", "Waiting for the Persona's answer"), st("confirmed", 2.4, 0.0, "Confirmed", "The Persona answered yes", END),
        st("rejected", 2.4, 1.1, "Rejected", "With the Persona's reason code", OFF), st("withdrawn", 2.4, 2.2, "Withdrawn by Norvia", "Norvia took it back before an answer", OFF, 230),
        st("cancelled", 3.7, 0.0, "Cancelled by Norvia", "Norvia cancelled it before collection", OFF, 230),
    ],
    groups=[], 
    edges=[
        ("s0", "open", "ReceiveCarrierBooking"), ("open", "confirmed", "Answer: confirm"), ("open", "rejected", "Answer: reject"),
        ("open", "withdrawn", "WithdrawnByNorvia"), ("confirmed", "cancelled", "CancelledByNorvia"),
    ],
)

SPECS["simulator-state-run"] = dict(
    title="Simulator · Simulation run lifecycle",
    subtitle="Only one run is active at a time. A run can be cancelled but not resumed after a restart, and its log stays after a refresh.",
    footnote=FOOT_STATES.replace("src/Tms/Norvia.Tms.Domain", "src/Simulator/Norvia.Simulator.Domain"), legend=LEG_STATES, cell=(340, 120),
    nodes=[
        start("s0", 0, 1.1), st("running", 1.1, 1.1, "Running", "Sending its plan, in a burst or paced"), st("completed", 2.4, 0.0, "Completed", "The whole plan was carried out", END),
        st("cancelled", 2.4, 1.1, "Cancelled", "The Presenter cancelled it", OFF), st("failed", 2.4, 2.2, "Failed", "Still running when the Simulator restarted", "orange"),
    ],
    groups=[],
    edges=[
        ("s0", "running", "StartRun"), ("running", "completed", "Complete"), ("running", "cancelled", "Cancel"), ("running", "failed", "Fail: AbandonActiveRuns"),
    ],
)

if __name__ == "__main__":
    render_all(SPECS, OUT, "", sys.argv[1:])
