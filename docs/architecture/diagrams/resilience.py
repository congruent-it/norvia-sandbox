#!/usr/bin/env python3
"""What happens when a hop fails, by failure pattern, and who finds out.

    python3 resilience.py    # writes failure-patterns.excalidraw and .png here

Matrix style (see README.md): the columns are the five ways a hop can fail, the rows are who finds out. Needs rsvg-convert for the PNG.
"""
import subprocess
from pathlib import Path

from excalidraw_matrix import render

OUT = Path(__file__).parent

SPEC = dict(
    title="Failure patterns",
    subtitle="What happens when a hop fails, and who finds out. Every hop of every Flow fails in one of five ways, and no alert is built yet, so a failure that reaches nobody waits to be found in the Integration console.",
    col_w=300, box_h=132,
    legend=[("Someone sees it in an application", "#fff3bf", "#f59f00", False), ("The console shows it", "#ffdeeb", "#d6336c", False), ("A caller is told", "#e9ecef", "#868e96", False), ("What is missing", "#f1f3f5", "#adb5bd", True)],
    footnote="Solid yellow is a person who sees it in an application. Dashed grey is what is missing: every alert is planned, none is built. Source of truth: docs/architecture/failure-behaviour.md, which says it per hop.",
    lanes=[
        dict(key="caller", palette="gray", name="The caller", sub="A partner, told in the same call"),
        dict(key="owner", palette="blue", name="The owner's user", sub="Planner, Finance or the Driver, in the application"),
        dict(key="operator", palette="pink", name="Integration operator", sub="In the console: the trail, failed runs, dead-letter queues"),
        dict(key="nobody", palette="lightgray", name="Nobody", sub="No alert is built", dashed=True),
    ],
    streams=[
        dict(name="From a partner", palette="blue", cols=[("Partner call", "A partner waits: P1 · P4 · E2 · P8"), ("Queued message", "A partner is told 202: P5 · P6")]),
        dict(name="Between applications", palette="green", cols=[("Sent, then reported", "Logic App sends out: P4 · P7 · P11 · E1 · E2"), ("Recorded by an owner", "Logic App calls the API: P9 · E1 · E2 · P12")]),
        dict(name="Events", palette="orange", cols=[("Event published", "Every outbox")]),
    ],
    steps=[
        ("a1", 0, "caller", 0, "auto", "Told in the same call", "The partner is told and sends again. Intake and answers retry the TMS three times first, and a 422 is an answer, not a failure"),
        ("a2", 0, "operator", 0, "auto", "View the run and the trail", "View only: a resubmit would create an order the Shipper believes was never received"),
        ("a3", 0, "nobody", 0, "planned", "No alert on failed runs", "Planned: failed requests on the Partner API, failed Logic App runs"),
        ("b1", 1, "caller", 0, "auto", "Told 202, and nothing more", "The Carrier is told at once, and never hears if recording fails later"),
        ("b2", 1, "operator", 0, "auto", "Dead-letter queue", "After 10 deliveries, or at once if unmappable. Resubmit to the queue, or discard"),
        ("b3", 1, "nobody", 0, "planned", "No alert on the dead-letter queue", "A delivered status that never maps leaves the order In transit: the headline incident"),
        ("c1", 2, "owner", 0, "person", "Sees Not delivered", "Booking, notification, cancellation or Trip version, or an invoice. Send it again, or phone the Carrier"),
        ("c2", 2, "operator", 0, "auto", "A failed run, if the report fails", "The item stays Requested or Issued and the run fails"),
        ("c3", 2, "nobody", 0, "planned", "No alert on failed runs", "The owner's list is the signal"),
        ("d1", 3, "owner", 0, "person", "The Driver sees the report's state", "Driver reports only: Refused with the TMS's code, or Not recorded"),
        ("d2", 3, "operator", 0, "auto", "Dead-lettered with the owner's code", "Resubmit to that subscription once the cause is fixed. Billing: the order stays Billable with no line in the ERP until then"),
        ("d3", 3, "nobody", 0, "planned", "No alert on the dead-letter queue", "Planned: failed runs and the dead-letter queues"),
        ("e2", 4, "operator", 0, "auto", "A missing hop on the trail", "Retried every second, forever. A failed cycle holds back every message behind it"),
        ("e3", 4, "nobody", 0, "planned", "No alert on the oldest message", "Planned: the oldest unpublished outbox message"),
    ],
    edges=[],
)

if __name__ == "__main__":
    out = OUT / "failure-patterns.excalidraw"
    render(SPEC, out, "/tmp/failure-patterns.svg")
    try:
        subprocess.run(["rsvg-convert", "-z", "1", "/tmp/failure-patterns.svg", "-o", str(out.with_suffix(".png"))], check=True)
    except FileNotFoundError:
        print("rsvg-convert not found: wrote the .excalidraw only")
    print("wrote", out.name)
