#!/usr/bin/env python3
"""The business automation model: which application performs each TMS and ERP process step.

    python3 business-automation-model.py            # writes ../business-automation-model.excalidraw
    python3 business-automation-model.py preview.svg  # also writes an SVG to check the layout
"""
import sys
from pathlib import Path

from excalidraw_matrix import render

steps = [
    # id, col, lane, slot, kind, title, sub
    ("a1", 0, "portal", 0, "person", "Submit a booking", "Shipper Persona, Partner API"),
    ("a2", 0, "int", 0, "auto", "Map to the Canonical Model", "P1 · APIM + intake Logic App"),
    ("a3", 0, "tms", 0, "auto", "Accept, or reject with every reason", "P1 · TMS intake API"),
    ("w1", 0, "wms", 0, "planned", "Fulfilment handoff", "WMS to intake, later"),

    ("b1", 1, "tms", 0, "auto", "Rate from the Shipper's Rate Card", "P2 · Freight Charge frozen"),
    ("b2", 1, "tms", 1, "person", "Assign to Own fleet or a Carrier", "P3 · Planner, with load check"),
    ("b3", 1, "tms", 2, "person", "Release the Shipment", "P3 · Planner"),
    ("e0", 1, "erp", 0, "planned", "Rate Cards as master data", "Seed file today, E3 sync later"),

    ("c1", 2, "portal", 0, "person", "Confirm or reject the booking", "P4 · Carrier Persona"),
    ("c2", 2, "int", 0, "auto", "Send the request, relay the answer", "P4 · two Logic Apps"),
    ("c3", 2, "tms", 0, "auto", "Booking Confirmed, Carrier cost frozen", "P4 · P9 · Carrier booking API"),

    ("d1", 3, "tms", 0, "auto", "Open the day's capacity", "P10 · Rosters, hour budgets"),
    ("d2", 3, "tms", 1, "person", "Build the Trip and dispatch it", "P11 · Planner"),
    ("d3", 3, "int", 0, "auto", "Send the Trip as a whole version", "P11 · Trip dispatch Logic App"),
    ("d4", 3, "drv", 0, "auto", "Receive the Trip", "P11 · Driver app Trip endpoint"),

    ("f2", 4, "portal", 0, "person", "Report collected, in transit, delivered", "P5 · Carrier Persona"),
    ("f3", 4, "int", 0, "auto", "Put the reports in order and relay", "P5 · P12 · Logic Apps, per Trip"),
    ("f4", 4, "tms", 0, "auto", "Move status and ETA forward", "P5 · Tracking events, Stale flag"),
    ("f1", 4, "drv", 0, "person", "Start the Trip, complete each Stop", "P12 · Driver, captures the Proof"),

    ("g1", 5, "portal", 0, "person", "Send the signed Proof of delivery", "P6 · Carrier Persona"),
    ("g2", 5, "int", 0, "auto", "Claim-check the files and relay", "P6 · Proof of delivery Logic App"),
    ("g3", 5, "tms", 0, "auto", "Store the current Proof", "P6 · earlier one superseded"),

    ("h1", 6, "portal", 0, "person", "Ask where a Transport Order stands", "P8 · Shipper Persona"),
    ("h2", 6, "int", 0, "auto", "Map the Tracking status", "P8 · APIM tracking API + policy"),
    ("h3", 6, "tms", 0, "auto", "Read status, ETA and Proof", "P8 · TMS Shipper tracking API"),

    ("i4", 7, "portal", 0, "auto", "Receive the message", "P7 · Shipper and Carrier Personas"),
    ("i3", 7, "int", 0, "auto", "Notify the Shipper, cancel the booking", "P7 · two Logic Apps"),
    ("i1", 7, "tms", 0, "auto", "Open an Exception", "P7 · failed, damaged, late"),
    ("i2", 7, "tms", 1, "person", "Resolve: re-plan, notify, cancel", "P7 · Planner"),

    ("j2", 8, "int", 0, "auto", "Hand over the billable line", "P9 · Billing Logic App"),
    ("j1", 8, "tms", 0, "auto", "Mark the order Billable", "P9 · Delivered, Rated, with Proof"),
    ("j3", 8, "erp", 0, "auto", "Keep the line Waiting", "E1 · ERP billing API"),

    ("m3", 9, "portal", 0, "auto", "Invoice lands in the inbox", "E1 · Shipper Persona"),
    ("m2", 9, "int", 0, "auto", "Send the Shipper invoice", "E1 · Shipper invoice Logic App"),
    ("m1", 9, "erp", 0, "auto", "Issue one invoice per Shipper", "E1 · monthly run or Invoice now"),
    ("m0", 9, "erp", 1, "person", "Correct with a Credit note", "E1 · Finance"),

    ("k1", 10, "portal", 0, "person", "Submit the Carrier invoice", "E2 · Carrier Persona"),
    ("k2", 10, "int", 0, "auto", "Relay the invoice and each line's outcome", "E2 · Carrier invoice Logic Apps"),
    ("k3", 10, "erp", 0, "auto", "Match each line to its Carrier cost", "E2 · cost frozen at booking, P4"),
    ("k4", 10, "erp", 1, "person", "Decide the lines that don't match", "E2 · Finance"),
]
edges = [
    ("a1", "a2", "v"), ("a2", "a3", "v"),
    ("w1", "a2", "gl", dict(dashed=True)),
    ("a3", "b1", "h"), ("b1", "b2", "v"), ("b2", "b3", "v"),
    ("e0", "b1", "gl", dict(dashed=True, off=14, ddy=14)),
    ("b3", "c2", "h"), ("c2", "c1", "v", dict(both=True)), ("c2", "c3", "v"),
    ("b3", "d2", "h"), ("d1", "d2", "v"),
    ("d2", "d3", "gr", dict(off=10, ddy=-14)), ("d3", "d4", "gr", dict(off=22, sdy=14)),
    ("c3", "f2", "h"),
    ("d4", "f1", "h"),
    ("f2", "f3", "v"), ("f1", "f3", "gr", dict(off=12)), ("f3", "f4", "v"),
    ("g1", "g2", "v"), ("g2", "g3", "v"),
    ("h1", "h2", "v", dict(both=True)), ("h2", "h3", "v", dict(both=True)),
    ("i1", "i2", "v"), ("i2", "i3", "gr", dict(off=12)), ("i3", "i4", "v"),
    ("j1", "j2", "v"), ("j2", "j3", "gl", dict(off=12)),
    ("j3", "m1", "h"), ("m1", "m2", "v"), ("m2", "m3", "v"), ("m1", "m0", "v"),
    ("k1", "k2", "v", dict(both=True)), ("k2", "k3", "v"), ("k3", "k4", "v"),
]

SPEC = dict(
    title="Norvia Logistics · Business automation model",
    subtitle="Which application performs each business process step. Rows are applications, columns are the TMS "
             "and ERP business processes. Every hand-off between applications runs through the integration layer.",
    footnote="Solid arrows are built flows, dashed grey arrows are planned or reference data. Sources: docs/processes "
             "(P1–P12, E1–E2), docs/architecture/c4.md, GLOSSARY.md and ADRs 0001, 0002 and 0012.",
    lanes=[
        dict(key="portal", palette="gray", name="Partner portal", sub="Shippers and Carriers,\none Persona per party"),
        dict(key="int", palette="violet", name="Integration layer", sub="API Management,\nLogic Apps, Service Bus"),
        dict(key="tms", palette="blue", name="TMS", sub="Planner and\nCustomer service"),
        dict(key="erp", palette="green", name="ERP", sub="Finance"),
        dict(key="drv", palette="teal", name="Driver app", sub="Driver, on the road"),
        dict(key="wms", palette="lightgray", name="WMS", sub="Not built yet", dashed=True),
    ],
    streams=[
        dict(name="Order to delivery", palette="blue", cols=[
            ("Receive the order", "P1"), ("Rate and plan", "P2 · P3"), ("Book the Carrier", "P4 · Carrier only"),
            ("Dispatch the Trip", "P10 · P11 · Own fleet only"), ("Execute and track", "P5 · P12"),
            ("Prove delivery", "P6"), ("Show the Shipper", "P8 · any time"), ("Resolve exceptions", "P7")]),
        dict(name="Order to cash", palette="green", cols=[("Hand over to the ERP", "P9"), ("Invoice the Shipper", "E1")]),
        dict(name="Carrier procure to pay", palette="orange", cols=[("Settle the Carrier", "E2")]),
    ],
    cross=[
        dict(name="Integration console", sub="Integration operator", palette="pink",
             headline="Watches every hop of every process above, on one Message trail per correlation ID",
             detail="Finds the trail, shows failed Logic App runs and dead-lettered messages, and resubmits or discards them. "
                    "It reads the estate's telemetry and stores nothing."),
        dict(name="Simulator", sub="Presenter", palette="orange",
             headline="Plays the outside world and Norvia's people through the estate's real doors",
             detail="Books as each Shipper, answers and tracks as each Carrier, plans as the Planner, drives as each Driver "
                    "and invoices as Finance, so every run leaves a Message trail."),
    ],
    steps=steps,
    edges=edges,
    edge_labels=[("Own fleet", "d2", -64, -22)],
)

if __name__ == "__main__":
    render(SPEC, Path(__file__).parent.parent / "business-automation-model.excalidraw", sys.argv[1] if len(sys.argv) > 1 else None)
