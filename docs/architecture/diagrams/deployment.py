#!/usr/bin/env python3
"""The deployment view and the local-versus-Azure map.

    python3 deployment.py [name ...]    # writes <name>.excalidraw and .png; no names draws all

Graph style (see README.md). The PNGs need rsvg-convert.
"""
import sys
from pathlib import Path

from excalidraw_graph import render_all
from excalidraw_matrix import PALETTE, PERSON

OUT = Path(__file__).parent
L = lambda label, key: (label, PALETTE[key][2], PALETTE[key][3], False)
SPECS = {}

V = "violet"
SPECS["deployment"] = dict(
    title="Deployment view · The estate in Azure",
    subtitle="One resource group per environment. The estate Bicep owns the integration layer and what it leans on, and the Aspire AppHost owns the applications, "
             "Azure SQL and the TMS's storage (ADR 0004).",
    footnote="Source of truth: infra/estate.bicep and infra/estate/, src/Norvia.AppHost/AppHost.cs and docs/adr (0004, 0021, 0024). azd deploys the estate first, then Aspire provisions the apps into the same resource group.",
    legend=[L("Integration layer", V), L("TMS", "blue"), L("ERP", "green"), L("Driver app", "teal"), L("Partner portal", "gray"), L("Simulator", "orange"), L("Integration console", "pink"), ("A person", *PERSON, False)],
    cell=(390, 120),
    nodes=[
        dict(id="person", col=0, row=2.8, kind="person", w=240, title="Presenter, Integration operator", sub="Open the UIs through the Entra gate"),
        dict(id="entra", col=0, row=4.6, palette="gray", w=240, title="Microsoft Entra ID", sub="App registrations and app roles, a client app per Persona, Easy Auth in front of the UIs. Tenant-wide, not in the resource group"),
        dict(id="portal", col=1.3, row=0.4, palette="gray", w=250, title="Partner portal", sub="[Container App]"),
        dict(id="tms", col=1.3, row=1.5, palette="blue", w=250, title="TMS API and UI", sub="[Container Apps]"),
        dict(id="erp", col=1.3, row=2.6, palette="green", w=250, title="ERP API and UI", sub="[Container Apps]"),
        dict(id="driver", col=1.3, row=3.7, palette="teal", w=250, title="Driver app API and UI", sub="[Container Apps]"),
        dict(id="sim", col=1.3, row=4.8, palette="orange", w=250, title="Simulator", sub="[Container App] a demo tool, can be switched off"),
        dict(id="console", col=1.3, row=5.9, palette="pink", w=250, title="Integration console", sub="[Container App]"),
        dict(id="jobs", col=1.3, row=7.0, palette="lightgray", w=250, title="Migration jobs ×5", sub="[Container Apps jobs] One per database, run by azd's postdeploy hook"),
        dict(id="apim", col=2.9, row=0.4, palette=V, w=290, title="API Management", sub="[Developer tier, one unit] The Norvia Partner API and its policies. It can't be stopped, only deleted (ADR 0021)"),
        dict(id="las", col=2.9, row=1.6, palette=V, w=290, title="14 Logic Apps", sub="[Logic Apps Standard] One Flow each, each with its own WS1 plan and storage account"),
        dict(id="bus", col=2.9, row=2.9, palette=V, w=290, title="Service Bus namespace", sub="[Standard] Five topics with filtered subscriptions, and the carrier-tracking and carrier-proof-of-delivery queues"),
        dict(id="inbound", col=2.9, row=4.1, palette=V, w=290, title="Inbound storage", sub="[Storage account] The claim-check container for Carrier Proofs of delivery"),
        dict(id="kvs", col=2.9, row=5.2, palette=V, w=290, title="Key Vaults", sub="The Logic Apps' content secret, the Partner portal's keys, the Driver app's endpoint key"),
        dict(id="mon", col=2.9, row=6.4, palette=V, w=290, title="Log Analytics and Application Insights", sub="Every trace unsampled, kept for integrationRetentionDays"),
        dict(id="ids", col=2.9, row=7.5, palette=V, w=290, title="Managed identities", sub="One per caller, holding only the app roles it needs"),
        dict(id="sql", col=4.6, row=1.2, palette="blue", w=270, title="Azure SQL", sub="[Serverless] One server, five databases: tms, erp, driver-app, partner-portal, simulator"),
        dict(id="store", col=4.6, row=2.9, palette="blue", w=270, title="Storage account", sub="[Blob] The TMS's Proof of delivery files, cooled after 30 days"),
    ],
    groups=[
        dict(id="env", title="Owned by the Aspire AppHost: Container Apps environment 'apps'", palette="orange", members=["portal", "tms", "erp", "driver", "sim", "console", "jobs"]),
        dict(id="bicep", title="Owned by the estate Bicep", palette=V, members=["apim", "las", "bus", "inbound", "kvs", "mon", "ids"]),
        dict(id="data", title="Owned by the Aspire AppHost: data", palette="blue", members=["sql", "store"]),
        dict(id="rg", title="Resource group rg-<environment>", palette="gray", members=["env", "bicep", "data"]),
    ],
    edges=[
        ("person", "entra", "sign in"), ("person", "tms", "browser, through the gate"),
        ("portal", "apim", "HTTPS, the Persona's token and key"),
        ("apim", "las", "HTTPS, managed identity"), ("apim", "inbound", "claim-check file"), ("apim", "bus", "queues"), ("apim", "tms", "Shipper queries"),
        ("las", "tms", "commands and reports"), ("las", "erp", "ERP billing API"), ("las", "driver", "Trip endpoint"), ("las", "portal", "Persona endpoints"),
        ("tms", "bus", "events, through the outbox"), ("erp", "bus", "events"), ("driver", "bus", "events"), ("console", "bus", "dead-letter queues"),
        ("tms", "sql", "each application its own database"), ("jobs", "sql", "migrations"), ("tms", "store", "Proof of delivery files"),
        ("las", "mon", "traces"), ("console", "mon", "Message trails"), ("las", "kvs", "secrets"),
    ],
)

# ------------------------------------------------------------------ local versus Azure
AZ, LOC = "gray", "orange"
rows = [
    ("Norvia Partner API", "[API Management policies]", "LocalPartnerApi", "[ASP.NET Core app] The same routes and checks, in the same order", "0023"),
    ("Intake and Carrier answer Logic Apps", "[P1, P4]", "LocalPartnerApi, in the request", "Calls the same maps and relay classes the Logic Apps' local functions wrap", "0019"),
    ("Carrier tracking and Proof of delivery Logic Apps", "[P5, P6]", "LocalTrackingWorker, LocalProofOfDeliveryWorker", "[hosted workers] LocalPartnerApi accepts and queues", "0031, 0032"),
    ("Booking, Shipper notification and cancellation Logic Apps", "[P4, P7]", "LocalBookingWorker, LocalShipperNotificationWorker, LocalCarrierCancellationWorker", "[hosted workers]", "0030, 0033, 0034"),
    ("Trip dispatch and Driver reports Logic Apps", "[P11, P12]", "LocalTripDispatchWorker, LocalDriverReportsWorker", "[hosted workers] The reports worker reads the session-enabled subscription", "0036, 0038"),
    ("Billing, Carrier cost, Shipper invoice and Carrier invoice Logic Apps", "[E1, E2, P9]", "LocalBillingWorker, LocalCarrierCostWorker, LocalShipperInvoiceWorker, LocalCarrierInvoiceWorker, LocalCarrierInvoiceOutcomeWorker", "[hosted workers]", "0043, 0045, 0046"),
    ("Service Bus", "[topics, subscriptions, queues]", "Two Service Bus emulators", "messaging and messaging-2: each accepts only 10 concurrent connections", "0004"),
    ("Inbound storage and the TMS's Proof of delivery files", "[Blob storage]", "Azurite", "With the inbound-proofs-of-delivery container and the TMS's own", "0032"),
    ("Azure SQL", "[five databases]", "SQL Server in a container", "With DbGate beside it. Migrations run as a project the apps wait for", "0004"),
    ("Entra tokens and app roles", "[a client app per Persona, a managed identity per caller]", "Tokens signed with one local key", "The TMS API trusts that issuer. Each caller still asks for a token with its own roles", "0005"),
    ("Entra sign-in in front of the UIs and the Driver app", "[Easy Auth]", "A local operator, and a Driver sign-in page", "Refused outside Development", "0024, 0026, 0037"),
    ("Key Vault", "[Persona keys, endpoint credentials]", "Keys the AppHost generates for the run", "Passed to both sides, from the Personas in the seed file", "0023"),
    ("Application Insights and Log Analytics", "[every trace, unsampled]", "The Aspire dashboard", "Over OTLP. The console searches it with DashboardTrailSource", "0027"),
    ("Logic Apps management API", "[opening a run from the console]", "Nothing", "There are no runs to open, only the workers' traces", "0027"),
]
nodes, edges = [], []
for i, (at, asub, lt, lsub, adr) in enumerate(rows):
    nodes.append(dict(id=f"a{i}", col=0, row=i * 0.95, palette=AZ, w=330, title=at, sub=asub))
    nodes.append(dict(id=f"l{i}", col=1.25, row=i * 0.95, palette=LOC, w=400, title=lt, sub=lsub))
    edges.append((f"a{i}", f"l{i}", f"ADR {adr}"))
SPECS["local-vs-azure"] = dict(
    title="Local versus Azure · What plays each Azure piece",
    subtitle="Under Aspire there is no subscription, no API Management, no Logic Apps runtime and no Entra. Stand-ins run the same code, so a booking leaves the same Message trail.",
    footnote="Source of truth: docs/architecture/local-vs-azure.md and the ADRs it cites. The applications themselves are the same code in both places and need no row.",
    legend=[L("In Azure", AZ), L("Under Aspire, on a developer's machine", LOC)],
    cell=(420, 118), nodes=nodes, groups=[], edges=edges,
)

if __name__ == "__main__":
    render_all(SPECS, OUT, "", sys.argv[1:])
