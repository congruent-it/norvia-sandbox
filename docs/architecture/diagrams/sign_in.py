#!/usr/bin/env python3
"""How people and the Simulator sign in, as three sequence diagrams.

    python3 sign_in.py [name ...]    # writes sign-in-<name>.excalidraw and .png; no names draws all

Sequence style (see README.md). The PNGs need rsvg-convert.
"""
import sys
from pathlib import Path

from excalidraw_matrix import PALETTE, PERSON
from excalidraw_sequence import render_all

OUT = Path(__file__).parent
L = lambda label, key: (label, PALETTE[key][2], PALETTE[key][3], False)
LEGEND_ARROWS = "Solid arrows are calls, dashed arrows are replies. No sign-in holds a client secret."
SPECS = {}

P = lambda i, t, pal="violet", sub="", kind=None: dict(id=i, title=t, palette=pal, sub=sub, kind=kind)
EASY = P("easy", "Easy Auth", "gray", "Container Apps authentication")

SPECS["easy-auth"] = dict(
    title="Sign-in 1 · A person signs in to a UI with Easy Auth",
    subtitle="Shown for the Integration console. The demo UIs, the ERP UI and the Simulator sign in the same way and differ only in the role they check.",
    footnote=LEGEND_ARROWS + " Source of truth: ADR 0024 and 0026, and docs/architecture/identity-and-access.md.",
    legend=[("A person", *PERSON, False), L("Outside Norvia", "gray"), L("Integration console", "pink")],
    participants=[P("operator", "Integration operator", "gray", "the presenter, in a demo", "person"), EASY,
                  P("entra", "Microsoft Entra ID", "gray", "Norvia Integration console registration"),
                  P("console", "Integration console", "pink", "Container App"), P("azure", "Azure", "gray", "Log Analytics, Service Bus, ARM")],
    steps=[
        ("phase", "Signing in"),
        ("msg", "operator", "easy", "Opens the console's address"),
        ("self", "easy", "No session: sends the browser to Entra and asks for an ID token only, since it holds no client secret"),
        ("msg", "easy", "entra", "Sign-in request"),
        ("self", "entra", "Refuses a user with no role on the registration (appRoleAssignmentRequired), signed in to the tenant or not"),
        ("msg", "entra", "easy", "ID token with the user's app roles, to the registered callback", dict(dashed=True)),
        ("self", "easy", "Starts a session"),
        ("phase", "Using it"),
        ("msg", "operator", "easy", "Opens a page"),
        ("msg", "easy", "console", "The request, with the user and roles in X-MS-CLIENT-PRINCIPAL. Container Apps strips that header from a request from outside"),
        ("self", "console", "Every page needs IntegrationConsole.Read. A user without it sees which role is missing"),
        ("msg", "console", "azure", "Reads telemetry, queues and runs as its own managed identity, never with the operator's token"),
        ("msg", "console", "operator", "The page", dict(dashed=True)),
        ("phase", "Locally"),
        ("self", "console", "No Easy Auth: the AppHost signs the developer in as a local operator with both roles. It refuses those settings outside Development"),
    ],
)

SPECS["driver"] = dict(
    title="Sign-in 2 · A Driver signs in to the Driver app",
    subtitle="The Driver ID is the Driver's app role in the token, so the Driver app never takes it from a request.",
    footnote=LEGEND_ARROWS + " Source of truth: ADR 0037 and docs/architecture/identity-and-access.md.",
    legend=[("A person", *PERSON, False), L("Outside Norvia", "gray"), L("Driver app", "teal"), L("Integration layer", "violet")],
    participants=[P("driver", "Driver", "teal", "Norvia Entra test user", "person"), EASY,
                  P("entra", "Microsoft Entra ID", "gray", "Norvia Driver app registration"),
                  P("app", "Driver app", "teal", "Container App"), P("bus", "Service Bus", "violet", "driver-app-events")],
    steps=[
        ("phase", "Signing in"),
        ("msg", "driver", "easy", "Opens the Driver app"),
        ("msg", "easy", "entra", "Sign-in request, asking for an ID token only"),
        ("self", "entra", "Refuses a user with no role. A Driver holds exactly one role, with their Driver ID as its value (DRV-0001)"),
        ("msg", "entra", "easy", "ID token carrying that one role", dict(dashed=True)),
        ("phase", "Using it"),
        ("msg", "easy", "app", "The request, with the user and roles in X-MS-CLIENT-PRINCIPAL"),
        ("self", "app", "Turns the role into the driverId claim. Every screen needs it: a user without it sees a page that says so"),
        ("self", "app", "Shows only that Driver's Trips and takes their reports, with the Driver ID from the token and never from the request"),
        ("msg", "app", "driver", "Their Trips", dict(dashed=True)),
        ("msg", "app", "bus", "Each report, with the Driver ID, through the Driver app's outbox", dict(dashed=True)),
        ("phase", "Locally"),
        ("self", "app", "No Entra: a sign-in page lists the seeded Drivers and signs the chosen one in with a cookie holding the same driverId claim. Development only"),
    ],
)

SPECS["simulator"] = dict(
    title="Sign-in 3 · The Simulator reaches the estate",
    subtitle="One managed identity, and a client app for each Persona and each Driver that trusts it. The estate decides what each may do.",
    footnote=LEGEND_ARROWS + " Source of truth: ADR 0042, ADR 0005 and ADR 0037.",
    legend=[("A person", *PERSON, False), L("Outside Norvia", "gray"), L("Simulator", "orange"), L("Integration layer", "violet"), L("TMS", "blue"), L("Driver app", "teal")],
    participants=[P("presenter", "Presenter", "gray", "signed in with Easy Auth", "person"), P("sim", "Simulator", "orange", "managed identity"),
                  P("entra", "Microsoft Entra ID", "gray", "client apps, federated credentials"),
                  P("kv", "Partner portal's Key Vault", "gray", "subscription keys"), P("apim", "API Management", "violet", "Norvia Partner API"),
                  P("tms", "TMS", "blue", "TMS API"), P("app", "Driver app", "teal", "Driver app API")],
    steps=[
        ("phase", "The presenter"),
        ("msg", "presenter", "sim", "Starts a run. Needs Simulator.Run, checked as in sign-in 1"),
        ("phase", "As a Shipper or a Carrier Persona"),
        ("msg", "sim", "entra", "Asks for a token for the Persona's client app, which trusts the Simulator's identity"),
        ("msg", "entra", "sim", "Token with the roles the Persona already holds", dict(dashed=True)),
        ("msg", "sim", "kv", "Reads the Persona's subscription key"),
        ("msg", "sim", "apim", "Calls the Norvia Partner API with the token and the key"),
        ("self", "apim", "Takes the party from the token's azp, as it does for the Partner portal"),
        ("phase", "As the Planner"),
        ("msg", "sim", "entra", "Asks for a token with its own identity (TransportOrders.Manage)"),
        ("msg", "sim", "tms", "Assignments, releases, Trips and Exceptions, as the TMS UI's"),
        ("phase", "As a Driver"),
        ("msg", "sim", "entra", "Asks for a token for that Driver's client app"),
        ("msg", "entra", "sim", "Token with one app role: the Driver ID", dict(dashed=True)),
        ("msg", "sim", "app", "The Driver's actions on /v1/driver/trips"),
        ("self", "app", "Takes the Driver ID from that one role, as it does from a Driver's own sign-in"),
    ],
)

if __name__ == "__main__":
    render_all(SPECS, OUT, "sign-in-", sys.argv[1:])
