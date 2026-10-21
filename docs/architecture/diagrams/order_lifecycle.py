#!/usr/bin/env python3
"""The life of an order across the estate, as four sequence diagrams.

    python3 order_lifecycle.py [name ...]    # writes ../order-lifecycle-<name>.excalidraw and .png; no names draws all

Sequence style (see README.md). The PNGs need rsvg-convert.
"""
import sys
from pathlib import Path

from excalidraw_matrix import PALETTE, PERSON
from excalidraw_sequence import render_all

OUT = Path(__file__).parent.parent
L = lambda label, key: (label, PALETTE[key][2], PALETTE[key][3], False)
LEGEND = [L("Outside Norvia", "gray"), L("Integration layer", "violet"), L("TMS", "blue"), L("ERP", "green"), ("A person", *PERSON, False)]
LEGEND_ARROWS = "Solid arrows are calls, dashed arrows are replies and events. Every call between applications goes through the integration layer."
SPECS = {}

P = lambda i, t, pal="violet", sub="", kind=None: dict(id=i, title=t, palette=pal, sub=sub, kind=kind)
SHIPPER = P("shipper", "Shipper Persona", "gray", "Partner portal")
CARRIER = P("carrier", "Carrier Persona", "gray", "Partner portal")
APIM = P("apim", "API Management", "violet", "Norvia Partner API")
BUS = P("bus", "Service Bus", "violet", "topics and queues")
TMS = P("tms", "TMS", "blue", "TMS API")
ERP = P("erp", "ERP", "green", "ERP API")
PLANNER = P("planner", "Planner", "blue", "TMS UI", "person")
FINANCE = P("finance", "Finance", "green", "ERP UI", "person")

SPECS["booked"] = dict(
    title="Order lifecycle 1 · From a booking to a Booked Shipment",
    subtitle="A Shipper books, the TMS accepts and rates the order, the Planner assigns and releases it, and the Carrier confirms. P1 to P4, for a Carrier Shipment.",
    footnote=LEGEND_ARROWS + " Source of truth: docs/processes/tms (P1 to P4) and docs/architecture/c4.md.",
    legend=LEGEND,
    participants=[SHIPPER, APIM, P("intake", "Intake Logic App"), TMS, PLANNER, BUS, P("booking", "Carrier booking Logic App"), CARRIER, P("answer", "Carrier answer Logic App")],
    steps=[
        ("phase", "P1 · Transport Order intake"),
        ("msg", "shipper", "apim", "POST /partner/v1/transport-orders: the booking, in the partner's format"),
        ("msg", "apim", "intake", "Canonical call, with the Shipper from the token and a correlation ID"),
        ("msg", "intake", "tms", "Submit the Transport Order, mapped to the Canonical Model"),
        ("self", "tms", "Accepts it with a Transport Order number, or rejects it with every reason. The event goes to the outbox in the same transaction"),
        ("msg", "tms", "intake", "201 accepted, 200 repeat, or 422 rejected", dict(dashed=True)),
        ("msg", "intake", "apim", "ACCEPTED, ALREADY_BOOKED or REJECTED", dict(dashed=True)),
        ("msg", "apim", "shipper", "The same answer, in the same call", dict(dashed=True)),
        ("msg", "tms", "bus", "TransportOrderAccepted, within a second", dict(dashed=True)),
        ("phase", "P2 · Rating and P3 · Transport planning"),
        ("self", "tms", "The rating worker rates the order from the Shipper's Rate Card and freezes the Freight Charge. A pricing gap never rejects the booking"),
        ("msg", "planner", "tms", "Assign the Transport Order to a Carrier"),
        ("self", "tms", "Checks the assignment rules, makes the Shipment and publishes ShipmentPlanned"),
        ("msg", "planner", "tms", "Release the Shipment, with a Spot price if no Carrier Rate Card prices it"),
        ("self", "tms", "Shipment Released, Carrier booking Requested and Carrier cost frozen, in one transaction"),
        ("note", ["planner", "tms"], "An Own fleet Shipment is not booked: the Planner builds a Trip and dispatches it to the Driver app instead (P11)"),
        ("msg", "tms", "bus", "ShipmentReleased, carrying the Carrier cost", dict(dashed=True)),
        ("phase", "P4 · Carrier booking"),
        ("msg", "bus", "booking", "Subscription carrier-booking: the Shipment is assigned to a Carrier", dict(dashed=True)),
        ("msg", "booking", "carrier", "Booking request, in the Carrier's format, with its endpoint credential from Key Vault"),
        ("msg", "carrier", "booking", "200 received", dict(dashed=True)),
        ("msg", "booking", "tms", "Report the booking sent"),
        ("msg", "carrier", "answer", "POST /partner/v1/carrier-answers, through API Management: confirm"),
        ("msg", "answer", "tms", "Answer the Carrier booking, mapped to the Canonical Model"),
        ("self", "tms", "Carrier booking Confirmed and Shipment Booked, in one transaction"),
        ("msg", "tms", "bus", "CarrierBookingConfirmed", dict(dashed=True)),
    ],
)

SPECS["delivered"] = dict(
    title="Order lifecycle 2 · From Booked to Delivered, and what everyone sees",
    subtitle="The Carrier reports tracking and a Proof of delivery, the TMS moves the Shipment forward, and the Shipper reads where it stands. P5 to P8.",
    footnote=LEGEND_ARROWS + " Source of truth: docs/processes/tms (P5 to P8).",
    legend=LEGEND,
    participants=[SHIPPER, CARRIER, APIM, P("bus", "Service Bus and storage", "violet", "queues and a blob"), P("tracking", "Carrier tracking Logic App"), P("pod", "Carrier Proof of delivery Logic App"), TMS, PLANNER],
    steps=[
        ("phase", "P5 · Shipment tracking"),
        ("msg", "carrier", "apim", "POST /partner/v1/carrier-tracking: collected, in transit or delivered"),
        ("msg", "apim", "bus", "The message on the carrier-tracking queue, after the token and role checks"),
        ("msg", "apim", "carrier", "202 Accepted", dict(dashed=True)),
        ("msg", "bus", "tracking", "A tracking message is available", dict(dashed=True)),
        ("msg", "tracking", "tms", "Record the Tracking event, mapped from the Carrier's status code"),
        ("self", "tms", "Records the event and moves the Shipment's status and ETA forward, never backwards. The event goes to the outbox in the same transaction"),
        ("msg", "tms", "tracking", "200, also for a repeat", dict(dashed=True)),
        ("msg", "tracking", "bus", "Settle the message: complete, dead-letter or abandon", dict(dashed=True)),
        ("note", ["carrier", "tms"], "Own fleet Shipments: the Driver app reports the same Tracking events and Proofs of delivery through the Driver reports Logic App (P12)"),
        ("phase", "P6 · Proof of delivery"),
        ("msg", "carrier", "apim", "POST /partner/v1/carrier-proofs-of-delivery: a signed CMR, a signature or photos"),
        ("msg", "apim", "bus", "Stores the file as a blob and queues its reference (claim check)"),
        ("msg", "apim", "carrier", "202 Accepted", dict(dashed=True)),
        ("msg", "bus", "pod", "A Proof of delivery reference is available", dict(dashed=True)),
        ("msg", "pod", "bus", "Reads the blob, and deletes it once recorded"),
        ("msg", "pod", "tms", "Record the Proof of delivery: canonical metadata plus the files"),
        ("self", "tms", "Stores it as the current one, supersedes any earlier one, opens or joins an Exception for a reservation, and decides whether the order is Billable"),
        ("phase", "P8 · What the Shipper sees"),
        ("msg", "shipper", "apim", "GET /partner/v1/transport-orders/{number}"),
        ("msg", "apim", "tms", "Shipper tracking query, as API Management's identity"),
        ("msg", "tms", "apim", "Tracking status, progress, ETA and the current Proof of delivery", dict(dashed=True)),
        ("msg", "apim", "shipper", "The same, mapped to the partner format", dict(dashed=True)),
        ("phase", "P7 · When something goes wrong"),
        ("self", "tms", "A failed collection or delivery, damage, a Proof of delivery with reservations or a Late delivery opens an Exception"),
        ("msg", "planner", "tms", "Resolve it: re-plan, notify the Shipper, cancel the Carrier booking, or agree a new delivery window"),
        ("note", ["pod", "planner"], "Notifying the Shipper and cancelling a booking go out through the Shipper notification and Carrier cancellation Logic Apps, and the TMS records what became of each"),
    ],
)

SPECS["invoice"] = dict(
    title="Order lifecycle 3 · From delivered to a Shipper invoice",
    subtitle="The TMS decides when an order is Billable, and the ERP does everything after that. P9 and E1.",
    footnote=LEGEND_ARROWS + " Source of truth: docs/processes/tms/p9-freight-settlement.md and docs/processes/erp/e1-invoicing.md.",
    legend=LEGEND,
    participants=[TMS, BUS, P("billing", "Billing Logic App"), ERP, FINANCE, P("invlogic", "Shipper invoice Logic App"), SHIPPER],
    steps=[
        ("phase", "P9 · Freight settlement"),
        ("self", "tms", "Delivered, Rated, with a current Proof of delivery and no open Exception: the order becomes Billable. Decided once, in the transaction of the event that completes it"),
        ("msg", "tms", "bus", "TransportOrderBillable, with the frozen Freight Charge", dict(dashed=True)),
        ("msg", "bus", "billing", "Subscription billing", dict(dashed=True)),
        ("msg", "billing", "erp", "Record the Billable line, as the event carries it"),
        ("self", "erp", "Billable line Waiting. Idempotent on the Transport Order number, and the Freight Charge is never recalculated"),
        ("msg", "erp", "billing", "201, or 200 for a repeat", dict(dashed=True)),
        ("phase", "E1 · Invoicing"),
        ("msg", "finance", "erp", "Invoice now for one Shipper. The monthly Invoice run does it for all of them"),
        ("self", "erp", "Issues one Shipper invoice, INV-year-number, with a line per Transport Order, and marks those lines Invoiced"),
        ("msg", "erp", "bus", "ShipperInvoiceIssued, through the ERP's outbox", dict(dashed=True)),
        ("msg", "bus", "invlogic", "Subscription shipper-invoice", dict(dashed=True)),
        ("msg", "invlogic", "shipper", "The invoice, in the Shipper's format, to the Persona's invoice endpoint"),
        ("msg", "shipper", "invlogic", "Acknowledged", dict(dashed=True)),
        ("msg", "invlogic", "erp", "Report the invoice Sent"),
        ("note", ["erp", "finance"], "Finance corrects an issued invoice with a Credit note, which takes the same path as CreditNoteIssued"),
    ],
)

SPECS["carrier-invoice"] = dict(
    title="Order lifecycle 4 · From a Carrier cost to a Carrier invoice outcome",
    subtitle="The TMS prices the Carrier booking and hands the cost over, the ERP matches the Carrier's invoice against it, and the Carrier is told. P9 and E2.",
    footnote=LEGEND_ARROWS + " Source of truth: docs/processes/tms/p9-freight-settlement.md and docs/processes/erp/e2-carrier-invoice-matching.md.",
    legend=LEGEND,
    participants=[TMS, BUS, P("costla", "Carrier cost Logic App"), ERP, P("outla", "Carrier invoice outcome Logic App"), FINANCE, P("cinvla", "Carrier invoice Logic App"), CARRIER],
    steps=[
        ("phase", "P4 and P9 · The Carrier cost"),
        ("self", "tms", "At release the TMS prices the Carrier booking from the Carrier Rate Card, or takes a Spot price, and freezes it on the booking"),
        ("self", "tms", "When the Carrier Shipment is Delivered, the TMS publishes CarrierCostIncurred in the same transaction"),
        ("msg", "tms", "bus", "CarrierCostIncurred", dict(dashed=True)),
        ("msg", "bus", "costla", "Subscription carrier-cost", dict(dashed=True)),
        ("msg", "costla", "erp", "Record the Carrier cost, as the event carries it"),
        ("self", "erp", "Carrier cost Open. Idempotent on the Carrier booking number"),
        ("phase", "E2 · Carrier invoice matching"),
        ("msg", "carrier", "cinvla", "POST /partner/v1/carrier-invoices, through API Management: one line per Carrier booking"),
        ("msg", "cinvla", "erp", "Receive the Carrier invoice, mapped to the Canonical Model"),
        ("self", "erp", "Matches each line to the Carrier cost of its Carrier booking, within the Matching tolerance. A line that doesn't match is To decide"),
        ("msg", "erp", "cinvla", "Received or Approved, with each line's result", dict(dashed=True)),
        ("msg", "cinvla", "carrier", "The same answer, in the same call", dict(dashed=True)),
        ("note", ["erp", "finance"], "Lines To decide wait for Finance, who accepts them at the invoiced or an adjusted amount, or rejects them with a reason. An unknown booking is matched again when its Carrier cost arrives"),
        ("msg", "finance", "erp", "Accept or reject the lines to decide"),
        ("self", "erp", "Approved once no line is left to decide. CarrierInvoiceApproved goes to the outbox in the same transaction"),
        ("msg", "erp", "bus", "CarrierInvoiceApproved", dict(dashed=True)),
        ("msg", "bus", "outla", "Subscription carrier-invoice-outcome", dict(dashed=True)),
        ("msg", "outla", "carrier", "The outcome of each line, to the Persona's invoice outcome endpoint"),
        ("msg", "outla", "erp", "Report the Carrier told"),
    ],
)

if __name__ == "__main__":
    render_all(SPECS, OUT, "order-lifecycle-", sys.argv[1:])
