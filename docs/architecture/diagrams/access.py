#!/usr/bin/env python3
"""Who may do what: the identity and access grid.

    python3 access.py [name ...]    # writes <name>.excalidraw and .png here; no names draws all

Grid style (see README.md). The PNGs need rsvg-convert.
"""
import sys
from pathlib import Path

from excalidraw_grid import render_all

OUT = Path(__file__).parent
SPECS = {}

R, D, W = "role", "read", "write"
COLUMNS = [
    dict(title="TMS API", sub="App roles", palette="blue"),
    dict(title="ERP API", sub="App roles", palette="green"),
    dict(title="Driver app API", sub="App roles", palette="teal"),
    dict(title="Logic App triggers", sub="App roles", palette="violet"),
    dict(title="Service Bus", sub="Azure roles", palette="violet"),
    dict(title="Blob storage", sub="Azure roles", palette="gray"),
    dict(title="Key Vault", sub="Secrets User", palette="gray"),
    dict(title="Telemetry and management", sub="Azure roles", palette="pink"),
]
TMS, ERP, DRV, TRG, BUS, BLOB, KV, MON = range(8)

def la(i, title, sub): return dict(id=i, title=title, sub=sub)

LOGIC_APPS = [
    la("intake", "intake", "Intake Logic App, P1"), la("answer", "answer", "Carrier answer, P4"), la("booking", "booking", "Carrier booking, P4"),
    la("cancel", "carrier-cancellation", "Carrier cancellation, P7"), la("notify", "shipper-notification", "Shipper notification, P7"),
    la("dispatch", "trip-dispatch", "Trip dispatch, P11"), la("tracking", "tracking", "Carrier tracking, P5"),
    la("pod", "proof-of-delivery", "Carrier Proof of delivery, P6"), la("reports", "driver-reports", "Driver reports, P12"),
    la("billing", "billing", "Billing, E1"), la("cost", "carrier-cost", "Carrier cost, P9"), la("sinv", "shipper-invoice", "Shipper invoice, E1"),
    la("cinv", "carrier-invoice", "Carrier invoice, E2"), la("cout", "carrier-invoice-outcome", "Carrier invoice outcome, E2"),
]
APPS = [
    la("apim", "apim", "API Management"), la("tmsapi", "tms-api", "TMS API, owned by the AppHost"), la("tmsui", "tms-ui", "TMS UI"),
    la("erp", "erp-web, erp-api", "ERP UI and API, owned by the AppHost"), la("driver", "driver-app", "Driver app"), la("portal", "partner-portal", "Partner portal"),
    la("sim", "simulator", "Simulator"), la("console", "integration-console", "Integration console"),
]
C = {}
def put(row, col, *items): C[(row, col)] = list(items)

put("intake", TMS, ("TransportOrders.Intake", R))
put("answer", TMS, ("CarrierBookings.Answer", R))
put("booking", TMS, ("CarrierBookings.Report", R)); put("booking", BUS, ("Subscription carrier-booking", D)); put("booking", KV, ("Each Carrier's endpoint key", D))
put("cancel", TMS, ("CarrierBookings.Report", R)); put("cancel", BUS, ("Subscription carrier-cancellation", D)); put("cancel", KV, ("Each Carrier's endpoint key", D))
put("notify", TMS, ("ShipperNotifications.Report", R)); put("notify", BUS, ("Subscription shipper-notification", D)); put("notify", KV, ("Each Shipper's endpoint key", D))
put("dispatch", TMS, ("Trips.ReportDelivery", R)); put("dispatch", BUS, ("Subscription trip-dispatch", D)); put("dispatch", KV, ("The Driver app's endpoint key", D))
put("tracking", TMS, ("TrackingEvents.Record", R)); put("tracking", BUS, ("Queue carrier-tracking", D))
put("pod", TMS, ("ProofsOfDelivery.Record", R)); put("pod", BUS, ("Queue carrier-proof-of-delivery", D)); put("pod", BLOB, ("Inbound container: read and delete", W))
put("reports", TMS, ("Trips.Start, Trips.End", R), ("TrackingEvents.Record, ProofsOfDelivery.Record", R)); put("reports", DRV, ("DriverReports.ReportOutcome", R))
put("reports", BUS, ("Subscription driver-reports", D)); put("reports", BLOB, ("Driver app's transit container: read and delete", W))
put("billing", ERP, ("BillableLines.Record", R)); put("billing", BUS, ("Subscription billing", D))
put("cost", ERP, ("CarrierCosts.Record", R)); put("cost", BUS, ("Subscription carrier-cost", D))
put("sinv", ERP, ("ShipperInvoices.Report", R)); put("sinv", BUS, ("Subscription shipper-invoice", D)); put("sinv", KV, ("Each Shipper's endpoint key", D))
put("cinv", ERP, ("CarrierInvoices.Receive", R))
put("cout", ERP, ("CarrierInvoices.Report", R)); put("cout", BUS, ("Subscription carrier-invoice-outcome", D)); put("cout", KV, ("Each Carrier's endpoint key", D))
put("apim", TMS, ("ShipperTracking.Read", R)); put("apim", TRG, ("TransportOrderIntake.Trigger, CarrierAnswer.Trigger, CarrierInvoice.Trigger", R))
put("apim", BUS, ("Queues carrier-tracking and carrier-proof-of-delivery: send", W)); put("apim", BLOB, ("Inbound container: write", W)); put("apim", MON, ("Application Insights: publish metrics", W))
put("tmsapi", BUS, ("Namespace: send, and receive to peek the subscriptions a reset waits on", W)); put("tmsapi", BLOB, ("The TMS's storage: contributor", W))
put("tmsui", TMS, ("TransportOrders.Manage", R)); put("tmsui", BLOB, ("The TMS's storage: reader", D))
put("erp", BUS, ("Send, through the outbox", W), ("Receive to peek the subscriptions a reset waits on", D))
put("driver", BUS, ("Topic driver-app-events: send", W)); put("driver", BLOB, ("Driver app's transit container: contributor", W)); put("driver", KV, ("The Driver app's endpoint key", D))
put("portal", KV, ("The Persona subscription keys", D)); put("portal", DRV, ("Demo.Fault", R))
put("sim", TMS, ("TransportOrders.Manage, Estate.Reset", R)); put("sim", ERP, ("ShipperInvoices.Manage, CarrierInvoices.Manage, Estate.Reset", R)); put("sim", DRV, ("Estate.Reset", R))
put("sim", KV, ("The Persona subscription keys, the Partner portal's reset key, the Shippers' endpoint keys", D))
put("console", BUS, ("Namespace: list and peek", D), ("Namespace: send, to resubmit", W)); put("console", MON, ("Log Analytics Reader", D), ("Logic Apps Standard Reader: opens a run, changes nothing", D))

SPECS["identity-access"] = dict(
    title="Identity and access · Who may do what",
    subtitle="Every caller is a managed identity. Each row holds only the roles its job needs: a Logic App calls one thin operation, reads its own subscription and a few secrets.",
    footnote="Source of truth: infra/estate/identities.bicep, the role assignments beside each resource in infra/estate/, and the AppHost's role assignments. Each Logic App's host identity (storage and its content secret) is left out. docs/architecture/identity-and-access.md has the detail.",
    legend=[("Holds an API role: may call", "role"), ("Reads or receives", "read"), ("Sends or writes", "write")],
    columns=COLUMNS,
    groups=[dict(title="Logic Apps: one identity each", palette="violet", rows=LOGIC_APPS), dict(title="Applications and API Management", palette="blue", rows=APPS)],
    cells=C,
)

if __name__ == "__main__":
    render_all(SPECS, OUT, "", sys.argv[1:])
