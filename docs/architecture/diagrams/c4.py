#!/usr/bin/env python3
"""The C4 diagrams of ../c4.md, in the Excalidraw graph style.

    python3 c4.py [name ...]    # writes c4-<name>.excalidraw and .png; no names draws them all

Needs rsvg-convert for the PNGs (the SVG preview is rendered by the generator, not by Excalidraw).
"""
import sys
from pathlib import Path

from excalidraw_graph import render_all
from excalidraw_matrix import PALETTE, PERSON, PLANNED

OUT = Path(__file__).parent
L = lambda label, key: (label, PALETTE[key][2], PALETTE[key][3], False)
LEG_PERSON = ("A person", *PERSON, False)
LEG_PLANNED = ("Planned, not built yet", *PLANNED, True)
FOOT = "Solid arrows are built, dashed grey arrows are planned. Source of truth: the code, infra/ and docs/adr. Terms are defined in GLOSSARY.md."

SPECS = {}

# ---------------------------------------------------------------- L1
SPECS["l1-system-context"] = dict(
    title="C4 L1 · System context",
    subtitle="Norvia's own systems, the parties outside Norvia and the people who use them. Applications never call each other: "
             "everything between them goes through the integration layer.",
    footnote=FOOT,
    legend=[LEG_PERSON, L("Outside Norvia", "gray"), L("A Norvia system", "blue"), LEG_PLANNED],
    cell=(370, 150),
    nodes=[
        dict(id="shipper", col=-0.5, row=1.5, palette="gray", title="Shipper", sub="Books transports, tracks them"),
        dict(id="carrier", col=-0.5, row=2.6, palette="gray", title="Carrier", sub="Carries Norvia's subcontracted Shipments"),
        dict(id="consignee", col=-0.5, row=3.7, palette="gray", title="Consignee", sub="Receives the goods"),
        dict(id="portal", col=0.9, row=2.6, palette="gray", title="Partner portal", sub="Plays every Shipper's and Carrier's own system, one Persona per party"),
        dict(id="driverApi", col=0.9, row=4.1, palette="gray", title="Driver app", sub="Trip endpoint"),
        dict(id="presenter", col=1.6, row=-0.6, kind="person", title="Presenter", sub="Runs the demo"),
        dict(id="operator", col=1.6, row=0.6, kind="person", title="Integration operator", sub="Watches the integration layer"),
        dict(id="console", col=1.6, row=1.6, palette="pink", title="Integration console", sub="Message trails and Logic App runs, from the estate's telemetry"),
        dict(id="simulator", col=5.0, row=0.6, palette="orange", title="Simulator", sub="Representative orders, sent through the estate's real doors"),
        dict(id="ais", col=2.3, row=2.9, palette="violet", title="Integration layer", sub="[API Management, Logic Apps, Service Bus]\nThe Norvia Partner API, every Flow, the Canonical Model"),
        dict(id="tms", col=3.7, row=2.9, palette="blue", title="TMS", sub="Transport Orders, Shipments, Carrier bookings, Proofs of delivery"),
        dict(id="erp", col=3.0, row=4.4, palette="green", title="ERP", sub="Master data, Contracts, Rate Cards, invoicing"),
        dict(id="wms", col=1.6, row=4.4, kind="planned", title="WMS", sub="Fulfilment Orders, Inventory"),
        dict(id="driverapp", col=4.4, row=4.4, palette="teal", title="Driver app", sub="Trips and Stops on the road"),
        dict(id="driver", col=5.8, row=4.4, kind="planned", title="Driver", sub="Drives Norvia's own fleet"),
    ],
    groups=[dict(id="norvia", title="Norvia Logistics B.V.", palette="blue", members=["operator", "console", "simulator", "ais", "tms", "erp", "wms", "driverapp"])],
    edges=[
        ("shipper", "portal", "is played by"), ("carrier", "portal", "is played by"),
        ("consignee", "carrier", "signs the Proof of delivery the Carrier sends"),
        ("presenter", "portal", "plays Shippers and Carriers", dict(sides=("l", "t"))),
        ("presenter", "tms", "plays the Planner and Customer service", dict(sides=("r", "t"))),
        ("presenter", "operator", "plays", dict(orient="v")), ("operator", "console", "follows Message trails"),
        ("presenter", "simulator", "runs a Simulation plan", dict(orient="h")),
        ("simulator", "ais", "books and answers as each Persona [HTTPS]", dict(orient="v")),
        ("simulator", "tms", "plans as the Planner [TMS API]", dict(orient="v")),
        ("simulator", "driverapp", "works the Trips as each Driver", dict(orient="v")),
        ("simulator", "erp", "runs Invoice now as Finance [ERP API]", dict(orient="v")),
        ("portal", "ais", "books, answers, tracks [Partner API]"),
        ("ais", "portal", "booking requests, notifications [HTTPS]"),
        ("ais", "tms", "commands, queries [HTTPS]"), ("tms", "ais", "events [Service Bus]"),
        ("console", "ais", "reads telemetry", dict(orient="v")),
        ("wms", "ais", "Fulfilment handoffs", dict(dashed=True, orient="v")),
        ("ais", "erp", "billable lines, Carrier costs", dict(dashed=True, orient="v")),
        ("ais", "driverapp", "dispatched Trips", dict(dashed=True, orient="v")), ("driverapp", "ais", "Driver reports", dict(dashed=True, orient="v")),
        ("driver", "driverapp", "uses", dict(dashed=True)),
    ],
)


# ---------------------------------------------------------------- L2 messages
V = "violet"
SPECS["l2-messages"] = dict(
    title="C4 L2 · Containers: messages",
    subtitle="How a message moves between the applications. An application takes commands as HTTP calls and publishes what happened "
             "to Service Bus; it never consumes from it. Each Logic App runs one Flow and ends in a call to the TMS API.",
    footnote=FOOT,
    legend=[L("Outside Norvia", "gray"), L("Integration layer", "violet"), L("TMS", "blue"), L("ERP", "green"), L("Driver app", "teal")],
    cell=(310, 110),
    nodes=[
        dict(id="portal", col=0, row=5.2, palette="gray", title="Partner portal", sub="[Container App, Blazor]\nPersona bookings and inboxes, Carrier and Shipper endpoints"),
        dict(id="portaldb", col=0, row=3.5, palette="gray", title="Partner portal database", sub="[Azure SQL]\nEach Persona's own records"),
        dict(id="apim", col=1, row=3.3, palette=V, title="Norvia Partner API", sub="[API Management, Developer tier]\nSubscription key and Entra token, party from the token"),
        dict(id="trackQ", col=2, row=4.3, palette=V, title="carrier-tracking", sub="[Service Bus queue]"),
        dict(id="inbound", col=2, row=5.5, palette=V, title="inbound-proofs-of-delivery", sub="[Blob container]\nClaim check"),
        dict(id="podQ", col=2, row=6.7, palette=V, title="carrier-proof-of-delivery", sub="[Service Bus queue]"),
        dict(id="intake", col=3, row=0.4, palette=V, title="Intake", sub="[Logic App] P1"),
        dict(id="answer", col=3, row=1.5, palette=V, title="Carrier answer", sub="[Logic App] P4"),
        dict(id="carrierInvoice", col=3, row=2.6, palette=V, title="Carrier invoice", sub="[Logic App] E2"),
        dict(id="tracking", col=3, row=4.3, palette=V, title="Carrier tracking", sub="[Logic App] P5"),
        dict(id="pod", col=3, row=6.0, palette=V, title="Carrier Proof of delivery", sub="[Logic App] P6"),
        dict(id="tmsapi", col=4, row=2.6, palette="blue", title="TMS API", sub="[Container App, Minimal API]\nCommands, Shipper tracking, transactional outbox"),
        dict(id="tmsweb", col=4, row=5.0, palette="blue", title="TMS UI", sub="[Container App, Blazor]\nPlanner and Customer service"),
        dict(id="tmsdb", col=5, row=3.5, palette="blue", title="TMS database", sub="[Azure SQL]"),
        dict(id="podfiles", col=5, row=4.9, palette="blue", title="Proof of delivery files", sub="[Blob container]"),
        dict(id="shTopic", col=6, row=1.2, palette=V, title="shipment-events", sub="[Service Bus topic]"),
        dict(id="tripTopic", col=6, row=3.4, palette=V, title="trip-events", sub="[Service Bus topic]"),
        dict(id="toTopic", col=6, row=4.6, palette=V, title="transport-order-events", sub="[Service Bus topic]"),
        dict(id="booking", col=7, row=0.3, palette=V, title="Carrier booking", sub="[Logic App] P4"),
        dict(id="notify", col=7, row=1.3, palette=V, title="Shipper notification", sub="[Logic App] P7"),
        dict(id="cancel", col=7, row=2.3, palette=V, title="Carrier cancellation", sub="[Logic App] P7"),
        dict(id="tripDispatch", col=7, row=3.4, palette=V, title="Trip dispatch", sub="[Logic App] P11"),
        dict(id="billing", col=7, row=4.6, palette=V, title="Billing", sub="[Logic App] E1"),
        dict(id="shipperInvoice", col=7, row=5.6, palette=V, title="Shipper invoice", sub="[Logic App] E1"),
        dict(id="cost", col=7, row=6.6, palette=V, title="Carrier cost", sub="[Logic App] P9"),
        dict(id="carrierOutcome", col=7, row=7.6, palette=V, title="Carrier invoice outcome", sub="[Logic App] E2"),
        dict(id="driverApi", col=8, row=3.4, palette="teal", title="Driver app", sub="Trip endpoint"),
        dict(id="erp", col=8, row=6.1, palette="green", title="ERP billing API"),
    ],
    groups=[
        dict(id="inLas", title="Logic Apps started by API Management", palette=V, members=["intake", "answer", "carrierInvoice", "tracking", "pod"]),
        dict(id="tmsb", title="TMS", palette="blue", members=["tmsapi", "tmsweb", "tmsdb", "podfiles"]),
        dict(id="topics", title="Service Bus topics", palette=V, members=["shTopic", "tripTopic", "toTopic"]),
        dict(id="outLas", title="Logic Apps started by a subscription", palette=V, members=["booking", "notify", "cancel", "tripDispatch", "billing", "shipperInvoice", "cost", "carrierOutcome"]),
    ],
    edges=[
        ("portal", "portaldb", "", dict(plain=True, orient="v")), ("portal", "apim", "HTTPS"),
        ("apim", "intake", "bookings"), ("apim", "answer", "Carrier answers"), ("apim", "carrierInvoice", "Carrier invoices"),
        ("apim", "trackQ", "Tracking events"), ("apim", "inbound", "file"), ("apim", "podQ", "claim check"),
        ("apim", "tmsapi", "Shipper queries"),
        ("trackQ", "tracking"), ("podQ", "pod"), ("inbound", "pod", "fetch, then delete"),
        ("intake", "tmsapi"), ("answer", "tmsapi"), ("tracking", "tmsapi"), ("pod", "tmsapi"),
        ("tmsapi", "shTopic", "publishes"), ("tmsapi", "tripTopic", "publishes"), ("tmsapi", "toTopic", "publishes"),
        ("tmsapi", "tmsdb", "", dict(plain=True)), ("tmsapi", "podfiles", "stores"),
        ("tmsweb", "tmsdb", "reads [query side]"), ("tmsweb", "podfiles", "reads"), ("tmsweb", "tmsapi", "changes things", dict(orient="v")),
        ("shTopic", "booking", "carrier-booking"), ("shTopic", "notify", "shipper-notification"), ("shTopic", "cancel", "carrier-cancellation"),
        ("tripTopic", "tripDispatch", "trip-dispatch"), ("tripDispatch", "driverApi", "Trip version [HTTPS]"),
        ("booking", "portal", "booking request", dict(via="bottom", sides=("r", "b"))),
        ("notify", "portal", "notification", dict(via="bottom", sides=("r", "b"))),
        ("cancel", "portal", "cancellation", dict(via="bottom", sides=("r", "b"))),
        ("booking", "tmsapi", "report", dict(via="top", sides=("r", "t"))),
        ("notify", "tmsapi", "report", dict(via="top", sides=("r", "t"))),
        ("cancel", "tmsapi", "report", dict(via="top", sides=("r", "t"))),
        ("tripDispatch", "tmsapi", "report", dict(via="top", sides=("r", "t"))),
        ("toTopic", "billing", "TransportOrderBillable"), ("shTopic", "cost", "CarrierCostIncurred"),
        ("billing", "erp"), ("shipperInvoice", "erp"), ("cost", "erp"), ("carrierOutcome", "erp"),
        ("carrierInvoice", "erp", "", dict(via="top", sides=("r", "t"))),
        ("shipperInvoice", "portal", "HTTPS", dict(via="bottom", sides=("r", "b"))),
        ("carrierOutcome", "portal", "HTTPS", dict(via="bottom", sides=("r", "b"))),
    ],
)


# ---------------------------------------------------------------- L2 identity, secrets and telemetry
SPECS["l2-identity-secrets-telemetry"] = dict(
    title="C4 L2 · Containers: identity, secrets and telemetry",
    subtitle="What every container leans on. No application holds a secret: each reaches Azure and the TMS with a managed identity.",
    footnote=FOOT,
    legend=[L("Outside Norvia", "gray"), L("Integration layer", "violet"), L("TMS and Partner portal", "blue"), L("Integration console", "pink")],
    cell=(400, 118),
    nodes=[
        dict(id="apps", col=0, row=0.3, palette="blue", title="TMS API, TMS UI, Partner portal", sub="[Container Apps]"),
        dict(id="las", col=0, row=2.7, palette="violet", title="Logic Apps"),
        dict(id="apim", col=0, row=1.5, palette="violet", title="API Management"),
        dict(id="console", col=0, row=4.1, palette="pink", title="Integration console", sub="[Container App, Blazor]"),
        dict(id="entra", col=2, row=0.2, palette="gray", title="Microsoft Entra ID", sub="App registrations and app roles, managed identities, Persona client apps, Easy Auth for the UIs"),
        dict(id="contentkv", col=2, row=3.8, palette="violet", title="Logic Apps' Key Vault", sub="Content share connection strings"),
        dict(id="partnerkv", col=2, row=1.4, palette="gray", title="Partner portal's Key Vault", sub="Persona subscription keys, Carrier and Shipper endpoint keys"),
        dict(id="insights", col=2, row=2.6, palette="violet", title="Application Insights", sub="[on Log Analytics]\nEvery trace, unsampled"),
        dict(id="lastorage", col=2, row=4.9, palette="violet", title="Logic Apps' storage", sub="[a storage account each]\nRuntime state, run history"),
        dict(id="arm", col=2, row=6.1, palette="gray", title="Logic Apps management API", sub="[Azure Resource Manager]"),
    ],
    groups=[dict(id="callers", title="Containers", palette="blue", members=["apps", "las", "apim", "console"])],
    edges=[
        ("apps", "entra", "tokens"), ("las", "entra", "tokens"),
        ("apim", "entra", "validates partner tokens, gets its own"),
        ("console", "entra", "Integration operators sign in [Easy Auth]", dict(via="top", sides=("r", "t"))),
        ("las", "contentkv"), ("las", "lastorage"), ("las", "partnerkv", "endpoint keys"),
        ("apps", "partnerkv", "reads its keys"),
        ("apps", "insights", "traces"), ("las", "insights", "traces"), ("apim", "insights", "traces"),
        ("console", "insights", "searches Message trails"), ("console", "arm", "opens runs"),
        ("arm", "lastorage", "", dict(plain=True, orient="v")),
    ],
)

# ---------------------------------------------------------------- L2 agents (planned)
SPECS["l2-agents"] = dict(
    title="C4 L2 · Containers: Agents (planned)",
    subtitle="How an Agent reads the estate and answers a person. An Agent reaches an application only through API Management's MCP server, "
             "holds read roles only, and proposes Suggestions that the person confirms.",
    footnote="Dashed grey is planned: ADR 0040 and ADR 0041 are Proposed and no Agent is built. Source of truth: docs/adr/0040 and 0041, and identity-and-access.md for the identities and roles. "
             "Locally an in-process runner and an MCP stand-in play Foundry and API Management's MCP server (ADR 0041). Terms are defined in GLOSSARY.md.",
    legend=[LEG_PERSON, LEG_PLANNED, L("Outside Norvia", "gray"), L("Integration layer", "violet"), L("A Norvia application", "blue")],
    cell=(400, 128),
    nodes=[
        dict(id="person", col=0, row=0.7, kind="person", title="Planner, Customer service, Finance, Integration operator", sub="Asks, reads a Suggestion, decides", w=250),
        dict(id="host", col=0, row=2.4, palette="blue", title="Host UIs", sub="[TMS UI, ERP UI, Integration console]\nEach owns its IAgentClient port, shows the Suggestion and deletes the run's conversation"),
        dict(id="agents", col=1, row=2.4, kind="planned", title="Agents", sub="[Foundry prompt agents, definitions in the repo]\nEach has its own Entra agent identity once published. No write tools"),
        dict(id="model", col=1, row=3.9, kind="planned", title="Model deployment", sub="[DataZoneStandard, EU region]"),
        dict(id="mcp", col=2, row=2.4, kind="planned", title="MCP server per Agent", sub="[API Management]\nChecks the Agent's token and read role and rate-limits it. Records the identity and tool name"),
        dict(id="entra", col=2, row=0.2, palette="gray", title="Microsoft Entra ID", sub="Agent identities, read roles of the agent views"),
        dict(id="insights", col=2, row=4.1, palette="violet", title="Application Insights", sub="Each tool call is a hop on a Message trail. Foundry traces, content recording off"),
        dict(id="tmsView", col=3, row=0.8, kind="planned", title="TMS agent view", sub="[read-only API group]\nNeeds an API Management front, which the TMS API has none of today"),
        dict(id="erpView", col=3, row=1.95, kind="planned", title="ERP agent view", sub="[read-only API group]"),
        dict(id="consoleView", col=3, row=3.1, kind="planned", title="Integration console agent API", sub="[read-only]"),
        dict(id="healthView", col=3, row=4.25, kind="planned", title="Estate health-check app agent API", sub="[read-only]"),
        dict(id="azure", col=4, row=3.7, palette="gray", title="Azure", sub="Resource Graph, Cost Management, run history, dead-letter queues, read under the application's own managed identity"),
    ],
    groups=[
        dict(id="foundry", title="Microsoft Foundry [Agent Service]", palette="gray", dashed=True, members=["agents", "model"]),
        dict(id="apim", title="API Management", palette="violet", dashed=True, members=["mcp"]),
        dict(id="views", title="Agent views, from an allowlist of fields", palette="blue", dashed=True, members=["tmsView", "erpView", "consoleView", "healthView"]),
    ],
    edges=[
        ("person", "host", "asks, then confirms a Suggestion", dict(orient="v")),
        ("host", "agents", "asks, and a Suggestion comes back: plain text, never a command", dict(dashed=True, both=True)),
                ("agents", "model", "", dict(dashed=True, plain=True, orient="v")),
        ("agents", "mcp", "tool calls [MCP, agent identity token]", dict(dashed=True)),
        ("mcp", "tmsView", "read", dict(dashed=True)), ("mcp", "erpView", "read", dict(dashed=True)),
        ("mcp", "consoleView", "read", dict(dashed=True)), ("mcp", "healthView", "read", dict(dashed=True)),
        ("consoleView", "azure", "reads", dict(dashed=True)), ("healthView", "azure", "reads", dict(dashed=True)),
        ("mcp", "entra", "validates the Agent's token", dict(dashed=True, orient="v")),
        ("mcp", "insights", "traces", dict(dashed=True, orient="v")),
    ],
)

# ---------------------------------------------------------------- L3 flows that come in through API Management
SPECS["l3-api-management-flows"] = dict(
    title="C4 L3 · Flows that come in through API Management",
    subtitle="The Norvia Partner API's policies, the Logic Apps they start or fill a queue for, the .NET local functions each calls, "
             "and the TMS API operations at the end. Every Flow: take it in, map it, call the TMS.",
    footnote=FOOT,
    legend=[L("Outside Norvia", "gray"), L("Integration layer", "violet"), L("TMS", "blue")],
    cell=(330, 118),
    nodes=[
        dict(id="portal", col=0, row=5.8, palette="gray", title="Partner portal", sub="[outside Norvia]"),
        dict(id="pSubmit", col=1, row=0.2, palette="violet", title="submit-transport-order", sub="[policy]\nPOST /transport-orders"),
        dict(id="pAnswer", col=1, row=2.7, palette="violet", title="answer-carrier-booking", sub="[policy]\nPOST /carrier-answers"),
        dict(id="pTrack", col=1, row=5.2, palette="violet", title="submit-carrier-tracking", sub="[policy]\nPOST /carrier-tracking"),
        dict(id="pPod", col=1, row=8.3, palette="violet", title="submit-carrier-proof-of-delivery", sub="[policy]\nPOST /carrier-proofs-of-delivery"),
        dict(id="pQuery", col=1, row=11.0, palette="violet", title="Shipper tracking", sub="[4 policies, Liquid maps]\nGET /transport-orders, /find, /{number}, /{number}/proof-of-delivery/files/{id}"),
        dict(id="trackQ", col=2, row=5.2, palette="violet", title="carrier-tracking", sub="[queue]"),
        dict(id="inbound", col=2, row=7.8, palette="violet", title="inbound-proofs-of-delivery", sub="[Blob container]"),
        dict(id="podQ", col=2, row=9.3, palette="violet", title="carrier-proof-of-delivery", sub="[queue]"),
        dict(id="intakeLa", col=3, row=0.2, palette="violet", title="transport-order-intake", sub="[workflow, HTTP trigger]"),
        dict(id="intakeFn", col=3, row=1.2, palette="violet", title="MapPartnerBooking, RelayTmsOutcome", sub="[local functions]"),
        dict(id="answerLa", col=3, row=2.7, palette="violet", title="carrier-answer", sub="[workflow, HTTP trigger]"),
        dict(id="answerFn", col=3, row=3.7, palette="violet", title="MapCarrierAnswer, RelayTmsAnswer", sub="[local functions]"),
        dict(id="trackLa", col=3, row=5.2, palette="violet", title="carrier-tracking", sub="[workflow, queue trigger]"),
        dict(id="trackFn", col=3, row=6.2, palette="violet", title="MapCarrierTracking", sub="[local function]"),
        dict(id="podLa", col=3, row=8.3, palette="violet", title="carrier-proof-of-delivery", sub="[workflow, queue trigger]"),
        dict(id="podFn", col=3, row=9.3, palette="violet", title="MapCarrierProofOfDelivery", sub="[local function]"),
        dict(id="tIntake", col=4.3, row=0.2, w=270, palette="blue", title="POST /v1/transport-orders"),
        dict(id="tAnswer", col=4.3, row=2.7, w=270, palette="blue", title="POST /v1/carrier-bookings/{number}/answer"),
        dict(id="tTrack", col=4.3, row=5.2, w=270, palette="blue", title="POST /v1/tracking-events"),
        dict(id="tPod", col=4.3, row=8.3, w=270, palette="blue", title="POST /v1/proofs-of-delivery"),
        dict(id="tQuery", col=4.3, row=11.0, w=270, palette="blue", title="GET /v1/shipper-tracking/..."),
    ],
    groups=[
        dict(id="apim", title="Norvia Partner API [API Management]", palette="violet", members=["pSubmit", "pAnswer", "pTrack", "pPod", "pQuery"]),
        dict(id="intake", title="Intake [Logic App] P1", palette="violet", members=["intakeLa", "intakeFn"]),
        dict(id="answerG", title="Carrier answer [Logic App] P4", palette="violet", members=["answerLa", "answerFn"]),
        dict(id="trackG", title="Carrier tracking [Logic App] P5", palette="violet", members=["trackQ", "trackLa", "trackFn"]),
        dict(id="podG", title="Carrier Proof of delivery [Logic App] P6", palette="violet", members=["inbound", "podQ", "podLa", "podFn"]),
        dict(id="tms", title="TMS API", palette="blue", members=["tIntake", "tAnswer", "tTrack", "tPod", "tQuery"]),
    ],
    edges=[
        ("portal", "pSubmit", "HTTPS"), ("portal", "pAnswer", "HTTPS"), ("portal", "pTrack", "HTTPS"),
        ("portal", "pPod", "HTTPS"), ("portal", "pQuery", "HTTPS"),
        ("pSubmit", "intakeLa", "managed identity"), ("pAnswer", "answerLa", "managed identity"),
        ("pTrack", "trackQ", "message, 202"), ("pPod", "inbound", "1. file"), ("pPod", "podQ", "2. claim check, 202"),
        ("pQuery", "tQuery", "managed identity"),
        ("intakeLa", "intakeFn", "", dict(plain=True)), ("answerLa", "answerFn", "", dict(plain=True)),
        ("trackQ", "trackLa"), ("trackLa", "trackFn", "", dict(plain=True)),
        ("podQ", "podLa"), ("inbound", "podLa", "fetch, delete once recorded"), ("podLa", "podFn", "", dict(plain=True)),
        ("intakeLa", "tIntake", "own managed identity"), ("answerLa", "tAnswer", "own managed identity"),
        ("trackLa", "tTrack", "own managed identity"), ("podLa", "tPod", "own managed identity"),
    ],
)

# ---------------------------------------------------------------- L3 flows that start from a subscription
SPECS["l3-subscription-flows"] = dict(
    title="C4 L3 · Flows that start from a subscription",
    subtitle="The TMS publishes events, a filtered subscription hands each to one Logic App, and the workflow ends in a report to the TMS API.",
    footnote=FOOT,
    legend=[L("Outside Norvia", "gray"), L("Integration layer", "violet"), L("TMS", "blue"), L("Driver app", "teal")],
    cell=(360, 118),
    nodes=[
        dict(id="tmsEvents", col=0, row=4.0, palette="blue", title="TMS API", sub="[transactional outbox]\npublishes events"),
        dict(id="sh", col=1, row=2.7, palette="violet", title="shipment-events", sub="[topic]"),
        dict(id="trip", col=1, row=8.4, palette="violet", title="trip-events", sub="[topic]"),
        dict(id="billing", col=1, row=5.4, palette="violet", title="Billing, Carrier cost", sub="[Logic Apps] E1, P9"),
        dict(id="subBooking", col=2, row=0.2, palette="violet", title="carrier-booking", sub="[subscription]\nassignedTo = Carrier"),
        dict(id="subNotify", col=2, row=2.7, palette="violet", title="shipper-notification", sub="[subscription]\neventType = ShipperNotificationRequested"),
        dict(id="subCancel", col=2, row=5.2, palette="violet", title="carrier-cancellation", sub="[subscription]\neventType = CarrierBookingCancelled or CarrierBookingWithdrawn"),
        dict(id="subMon", col=2, row=7.0, palette="violet", title="monitoring", sub="[subscription, on every topic]"),
        dict(id="subTrip", col=2, row=8.4, palette="violet", title="trip-dispatch", sub="[subscription]\neventType = TripDispatched"),
        dict(id="bookingLa", col=3, row=0.2, palette="violet", title="carrier-booking", sub="[workflow]"),
        dict(id="bookingFn", col=3, row=1.2, palette="violet", title="MapShipmentReleased", sub="[local function]"),
        dict(id="notifyLa", col=3, row=2.7, palette="violet", title="shipper-notification", sub="[workflow]"),
        dict(id="notifyFn", col=3, row=3.7, palette="violet", title="MapShipperNotificationRequested", sub="[local function]"),
        dict(id="cancelLa", col=3, row=5.2, palette="violet", title="carrier-cancellation", sub="[workflow]"),
        dict(id="cancelFn", col=3, row=6.2, palette="violet", title="MapCarrierCancellation", sub="[local function]"),
        dict(id="tripLa", col=3, row=8.4, palette="violet", title="trip-dispatch", sub="[workflow, no local function]"),
        dict(id="portal", col=4.3, row=1.4, palette="gray", title="Partner portal", sub="Carrier booking and cancellation endpoints, Shipper notification endpoint"),
        dict(id="kv", col=4.3, row=4.0, palette="gray", title="Partner portal's Key Vault", sub="endpoint credential per Carrier and Shipper"),
        dict(id="driverApi", col=4.3, row=7.5, palette="teal", title="Driver app", sub="Trip endpoint"),
        dict(id="driverKv", col=4.3, row=9.5, palette="teal", title="Driver app's Key Vault", sub="endpoint credential"),
        dict(id="tBooking", col=5.8, row=0.2, w=290, palette="blue", title="POST /v1/carrier-bookings/{number}/report-sent, report-not-delivered"),
        dict(id="tNotify", col=5.8, row=2.7, w=290, palette="blue", title="POST /v1/shipper-notifications/{id}/report-sent, report-not-delivered"),
        dict(id="tCancel", col=5.8, row=5.2, w=290, palette="blue", title="POST /v1/carrier-bookings/{number}/report-cancellation-told, report-cancellation-not-delivered"),
        dict(id="tTrip", col=5.8, row=8.4, w=290, palette="blue", title="POST /v1/trips/{number}/versions/{version}/report-delivered, report-not-delivered"),
    ],
    groups=[
        dict(id="booking", title="Carrier booking [Logic App] P4", palette="violet", members=["bookingLa", "bookingFn"]),
        dict(id="notify", title="Shipper notification [Logic App] P7", palette="violet", members=["notifyLa", "notifyFn"]),
        dict(id="cancel", title="Carrier cancellation [Logic App] P7", palette="violet", members=["cancelLa", "cancelFn"]),
        dict(id="tripDispatch", title="Trip dispatch [Logic App] P11", palette="violet", members=["tripLa"]),
        dict(id="tms", title="TMS API", palette="blue", members=["tBooking", "tNotify", "tCancel", "tTrip"]),
    ],
    edges=[
        ("tmsEvents", "sh", "publishes"), ("tmsEvents", "trip", "publishes"),
        ("sh", "subBooking"), ("sh", "subNotify"), ("sh", "subCancel"), ("sh", "subMon"), ("trip", "subMon"), ("trip", "subTrip"),
        ("sh", "billing", "CarrierCostIncurred", dict(orient="v")),
        ("subBooking", "bookingLa"), ("subNotify", "notifyLa"), ("subCancel", "cancelLa"), ("subTrip", "tripLa"),
        ("bookingLa", "bookingFn", "", dict(plain=True)), ("notifyLa", "notifyFn", "", dict(plain=True)), ("cancelLa", "cancelFn", "", dict(plain=True)),
        ("bookingLa", "kv", "credential"), ("notifyLa", "kv", "credential"), ("cancelLa", "kv", "credential"),
        ("bookingLa", "portal", "booking request"), ("notifyLa", "portal", "notification"), ("cancelLa", "portal", "cancellation"),
        ("bookingLa", "tBooking", "report"), ("notifyLa", "tNotify", "report"), ("cancelLa", "tCancel", "report"),
        ("tripLa", "driverKv", "credential"), ("tripLa", "driverApi", "Trip version"), ("tripLa", "tTrip", "report"),
    ],
)


# ---------------------------------------------------------------- L3 event topology
SPECS["l3-event-topology"] = dict(
    title="C4 L3 · Event topology",
    subtitle="Who publishes to which topic, which subscription filters what, and which Logic App reads each subscription. "
             "Every topic also has a monitoring subscription that takes every message.",
    footnote="Source of truth: infra/estate/messaging.bicep and the AppHost, and docs/architecture/events.md for the events each filter lets through. "
             "Every filter also passes a message resubmitted from the Integration console to the subscription it was dead-lettered from. "
             "The Carrier-tracking and Carrier Proof of delivery Logic Apps read queues, not topics.",
    legend=[L("TMS", "blue"), L("ERP", "green"), L("Driver app", "teal"), L("Integration layer", V)],
    cell=(400, 100),
    nodes=[
        dict(id="tms", col=0, row=2.5, palette="blue", title="TMS", sub="[transactional outbox]"),
        dict(id="erp", col=0, row=6.5, palette="green", title="ERP", sub="[transactional outbox]"),
        dict(id="driver", col=0, row=8.0, palette="teal", title="Driver app", sub="[transactional outbox]"),
        dict(id="tTo", col=1, row=0.0, palette=V, title="transport-order-events", sub="[topic]"),
        dict(id="tSh", col=1, row=2.5, palette=V, title="shipment-events", sub="[topic]"),
        dict(id="tTrip", col=1, row=5.0, palette=V, title="trip-events", sub="[topic]"),
        dict(id="tInv", col=1, row=6.5, palette=V, title="invoicing-events", sub="[topic]"),
        dict(id="tDrv", col=1, row=8.0, palette=V, title="driver-app-events", sub="[topic]"),
        dict(id="sBilling", col=2, row=0.0, palette=V, title="billing", sub="[subscription]\nTransportOrderBillable"),
        dict(id="sBooking", col=2, row=1.0, palette=V, title="carrier-booking", sub="[subscription]\nCarrier Shipments (assignedTo = Carrier)"),
        dict(id="sCancel", col=2, row=2.0, palette=V, title="carrier-cancellation", sub="[subscription]\nCarrierBookingCancelled, CarrierBookingWithdrawn"),
        dict(id="sNotify", col=2, row=3.0, palette=V, title="shipper-notification", sub="[subscription]\nShipperNotificationRequested"),
        dict(id="sCost", col=2, row=4.0, palette=V, title="carrier-cost", sub="[subscription]\nCarrierCostIncurred"),
        dict(id="sTrip", col=2, row=5.0, palette=V, title="trip-dispatch", sub="[subscription]\nTripDispatched"),
        dict(id="sInvoice", col=2, row=6.0, palette=V, title="shipper-invoice", sub="[subscription]\nShipperInvoiceIssued, CreditNoteIssued"),
        dict(id="sOutcome", col=2, row=7.0, palette=V, title="carrier-invoice-outcome", sub="[subscription]\nCarrierInvoiceApproved"),
        dict(id="sReports", col=2, row=8.0, palette=V, title="driver-reports", sub="[subscription, sessions]\nthe four Driver reports, keyed by Trip number"),
        dict(id="lBilling", col=3, row=0.0, palette=V, title="Billing", sub="[Logic App] E1"),
        dict(id="lBooking", col=3, row=1.0, palette=V, title="Carrier booking", sub="[Logic App] P4"),
        dict(id="lCancel", col=3, row=2.0, palette=V, title="Carrier cancellation", sub="[Logic App] P7"),
        dict(id="lNotify", col=3, row=3.0, palette=V, title="Shipper notification", sub="[Logic App] P7"),
        dict(id="lCost", col=3, row=4.0, palette=V, title="Carrier cost", sub="[Logic App] P9"),
        dict(id="lTrip", col=3, row=5.0, palette=V, title="Trip dispatch", sub="[Logic App] P11"),
        dict(id="lInvoice", col=3, row=6.0, palette=V, title="Shipper invoice", sub="[Logic App] E1"),
        dict(id="lOutcome", col=3, row=7.0, palette=V, title="Carrier invoice outcome", sub="[Logic App] E2"),
        dict(id="lReports", col=3, row=8.0, palette=V, title="Driver reports", sub="[Logic App] P12"),
    ],
    groups=[dict(id="bus", title="Service Bus", palette=V, members=["tTo", "tSh", "tTrip", "tInv", "tDrv", "sBilling", "sBooking", "sCancel", "sNotify", "sCost", "sTrip", "sInvoice", "sOutcome", "sReports"])],
    edges=[
        ("tms", "tTo", "publishes"), ("tms", "tSh", "publishes", dict(orient="h")), ("tms", "tTrip", "publishes"),
        ("erp", "tInv", "publishes"), ("driver", "tDrv", "publishes"),
        ("tTo", "sBilling"), ("tSh", "sBooking"), ("tSh", "sCancel"), ("tSh", "sNotify"), ("tSh", "sCost"), ("tTrip", "sTrip"),
        ("tInv", "sInvoice"), ("tInv", "sOutcome"), ("tDrv", "sReports"),
        ("sBilling", "lBilling"), ("sBooking", "lBooking"), ("sCancel", "lCancel"), ("sNotify", "lNotify"), ("sCost", "lCost"),
        ("sTrip", "lTrip"), ("sInvoice", "lInvoice"), ("sOutcome", "lOutcome"), ("sReports", "lReports"),
    ],
)


if __name__ == "__main__":
    render_all(SPECS, OUT, "c4-", sys.argv[1:])
