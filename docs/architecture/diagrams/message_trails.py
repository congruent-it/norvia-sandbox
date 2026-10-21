#!/usr/bin/env python3
"""The Message trails of the process docs, as sequence diagrams: the Hops of each flow, as the Integration console shows them, in time order.

    python3 message_trails.py [name ...]    # e.g. p9-1; writes message-trail-<name>.excalidraw and .png here; no names draws all

Sequence style (see README.md). Each spec belongs to the process doc named in its footnote, which shows it under Message trail. The PNGs need rsvg-convert.
"""
import sys
from pathlib import Path

from excalidraw_matrix import PALETTE
from excalidraw_sequence import render_all

OUT = Path(__file__).parent
L = lambda label, key: (label, PALETTE[key][2], PALETTE[key][3], False)
LEGEND = [L("Partner portal", "gray"), L("Integration layer", "violet"), L("TMS", "blue"), L("ERP", "green"), L("Driver app", "teal")]
LEGEND_ARROWS = "Solid arrows are calls, dashed arrows are replies and events. Each Hop is named application: operation."
SPECS = {}

P = lambda i, t, pal="gray", sub="": dict(id=i, title=t, palette=pal, sub=sub)

SPECS["e1-1"] = dict(
    title="E1 Invoicing \u00b7 A Billable line",
    subtitle="Flow billing. The Hops as the Integration console shows them, in time order.",
    footnote=LEGEND_ARROWS + " Source of truth: docs/processes/erp/e1-invoicing.md, its Message trail.",
    legend=LEGEND,
    groups=[("Flow billing", ["sub", "logic", "erp"])],
    participants=[
        P("sub", "subscription billing", "violet", "transport-order-events (topic)"),
        P("logic", "billing", "violet", "logic-billing"),
        P("erp", "POST /v1/billable-lines", "green", "erp-api"),
    ],
    steps=[
        ("msg", "sub", "logic", "A TransportOrderBillable message is available", dict(dashed=True)),
        ("self", "logic", "Track_the_billable_line (Transport Order number, Shipment number, Shipper as partyId)"),
        ("msg", "logic", "erp", "Record_the_billable_line (the message body as it is, X-Correlation-ID)"),
        ("self", "erp", "Billable line Waiting. Idempotent on the Transport Order number. Nothing is published"),
        ("msg", "erp", "logic", "201, 200 (repeat), or 409, 422, 400 with a stable code", dict(dashed=True)),
        ("msg", "logic", "sub", "Settle_the_message: complete, or dead-letter with the ERP's code"),
    ],
)

SPECS["e1-2"] = dict(
    title="E1 Invoicing \u00b7 Invoice now",
    subtitle="Flow invoicing. The Hops as the Integration console shows them, in time order.",
    footnote=LEGEND_ARROWS + " Source of truth: docs/processes/erp/e1-invoicing.md, its Message trail.",
    legend=LEGEND,
    groups=[("Flow invoicing", ["erp", "pub", "logic", "portal", "report"])],
    participants=[
        P("erp", "Invoice now", "green", "erp-web"),
        P("pub", "publish ShipperInvoiceIssued", "green", "erp-api"),
        P("logic", "shipper-invoice", "violet", "logic-shipper-invoice"),
        P("portal", "POST /shipper/v1/shipper-id/invoices", "gray", "partner-portal-web"),
        P("report", "POST /v1/shipper-invoices/number/report-sent", "green", "erp-api"),
    ],
    steps=[
        ("self", "erp", "IssueShipperInvoiceCommand: Waiting lines Invoiced, invoice numbered, outbox message, one transaction"),
        ("msg", "erp", "pub", "outbox, within a second", dict(dashed=True)),
        ("msg", "pub", "logic", "invoicing-events, subscription shipper-invoice", dict(dashed=True)),
        ("self", "logic", "Track_the_invoice, Map_the_invoice (MapShipperDocument), Complete_the_message"),
        ("self", "logic", "Get_the_Shipper_endpoint_credential (Key Vault)"),
        ("msg", "logic", "portal", "Send_to_the_Shipper (the invoice in the Shipper's format)"),
        ("msg", "portal", "logic", "200 acknowledged", dict(dashed=True)),
        ("msg", "logic", "report", "Report_the_outcome_to_the_ERP: Report_the_invoice_sent"),
        ("msg", "report", "logic", "200 (or 200 ignored for a repeat)", dict(dashed=True)),
    ],
)

SPECS["e1-3"] = dict(
    title="E1 Invoicing \u00b7 An invoice the Shipper never got",
    subtitle="Flow invoicing. The Hops as the Integration console shows them, in time order.",
    footnote=LEGEND_ARROWS + " Source of truth: docs/processes/erp/e1-invoicing.md, its Message trail.",
    legend=LEGEND,
    groups=[("Flow invoicing", ["logic", "portal", "report", "pub", "again"])],
    participants=[
        P("logic", "shipper-invoice", "violet", "logic-shipper-invoice"),
        P("portal", "POST /shipper/v1/shipper-id/invoices", "gray", "partner-portal-web"),
        P("report", "POST /v1/shipper-invoices/number/report-not-delivered", "green", "erp-api"),
        P("pub", "publish ShipperInvoiceNotDelivered", "green", "erp-api"),
        P("again", "Send again", "green", "erp-web"),
    ],
    steps=[
        ("msg", "logic", "portal", "Send_to_the_Shipper (retried three times on a timeout, 408, 429 or 5xx)"),
        ("msg", "portal", "logic", "no answer, or a client error", dict(dashed=True)),
        ("msg", "logic", "report", "Report_the_outcome_to_the_ERP: Report_the_invoice_not_delivered (status code, cause up to 500 characters)"),
        ("self", "report", "Invoice Not delivered, with the outbox message, one transaction"),
        ("msg", "report", "pub", "outbox, within a second", dict(dashed=True)),
        ("self", "again", "SendShipperInvoiceAgainCommand: back to Issued, ShipperInvoiceIssued published again, same correlation ID"),
    ],
)

SPECS["e2-1"] = dict(
    title="E2 Carrier invoice matching \u00b7 A Carrier cost",
    subtitle="Flow carrier-costs. The Hops as the Integration console shows them, in time order.",
    footnote=LEGEND_ARROWS + " Source of truth: docs/processes/erp/e2-carrier-invoice-matching.md, its Message trail.",
    legend=LEGEND,
    groups=[("Flow carrier-costs", ["sub", "logic", "erp"])],
    participants=[
        P("sub", "subscription carrier-cost", "violet", "shipment-events (topic)"),
        P("logic", "carrier-cost", "violet", "logic-carrier-cost"),
        P("erp", "POST /v1/carrier-costs", "green", "erp-api"),
    ],
    steps=[
        ("msg", "sub", "logic", "A CarrierCostIncurred message is available", dict(dashed=True)),
        ("self", "logic", "Track_the_carrier_cost, Check_the_carrier_cost"),
        ("msg", "logic", "erp", "Record_the_carrier_cost (the message body as it is, X-Correlation-ID)"),
        ("self", "erp", "Carrier cost Open. Undecided Unknown booking lines of that Carrier with that reference are matched again, in the same save"),
        ("msg", "erp", "logic", "201, 200 (repeat), or 409, 422, 400 with a stable code", dict(dashed=True)),
        ("msg", "logic", "sub", "Settle_the_message: complete, or dead-letter with the ERP's code"),
    ],
)

SPECS["e2-2"] = dict(
    title="E2 Carrier invoice matching \u00b7 A Carrier invoice",
    subtitle="Flow carrier-invoice. The Hops as the Integration console shows them, in time order.",
    footnote=LEGEND_ARROWS + " Source of truth: docs/processes/erp/e2-carrier-invoice-matching.md, its Message trail.",
    legend=LEGEND,
    groups=[("Flow carrier-invoice", ["portal", "apim", "logic", "erp", "pub"])],
    participants=[
        P("portal", "Submit Carrier invoice", "gray", "partner-portal-web"),
        P("apim", "Submit Carrier invoice", "violet", "apim-norvia"),
        P("logic", "carrier-invoice", "violet", "logic-carrier-invoice"),
        P("erp", "POST /v1/carrier-invoices", "green", "erp-api"),
        P("pub", "publish CarrierInvoiceApproved", "green", "erp-api"),
    ],
    steps=[
        ("msg", "portal", "apim", "POST /partner/v1/carrier-invoices (Persona token and subscription key)"),
        ("self", "apim", "token, role CarrierInvoices.Submit, Carrier ID from azp, correlation ID"),
        ("msg", "apim", "logic", "Canonical call, X-Carrier-ID and X-Correlation-ID"),
        ("self", "logic", "Refuse any caller but API Management, Track_the_invoice, Map_the_invoice (MapCarrierInvoice)"),
        ("msg", "logic", "erp", "Receive_at_the_ERP (Canonical Model submission)"),
        ("self", "erp", "Every line matched against the Carrier's Carrier costs, matched Carrier costs Invoiced, one transaction"),
        ("msg", "erp", "logic", "201 or 200 (repeat), each line's result, or a refusal code", dict(dashed=True)),
        ("self", "logic", "Relay_the_ERP_outcome (RelayErpInvoiceAnswer)"),
        ("msg", "logic", "apim", "Answer_the_Carrier: RECEIVED or APPROVED with each line's result, or a refusal", dict(dashed=True)),
        ("msg", "apim", "portal", "the same answer", dict(dashed=True)),
        ("msg", "erp", "pub", "outbox, within a second, when every line matched", dict(dashed=True)),
    ],
)

SPECS["e2-3"] = dict(
    title="E2 Carrier invoice matching \u00b7 Finance decides the lines left To decide",
    subtitle="Flow carrier-invoice. The Hops as the Integration console shows them, in time order.",
    footnote=LEGEND_ARROWS + " Source of truth: docs/processes/erp/e2-carrier-invoice-matching.md, its Message trail.",
    legend=LEGEND,
    groups=[("Flow carrier-invoice", ["ui", "pub"])],
    participants=[
        P("ui", "Accept or reject a line", "green", "erp-web"),
        P("pub", "publish CarrierInvoiceApproved", "green", "erp-api"),
    ],
    steps=[
        ("self", "ui", "AcceptCarrierInvoiceLineCommand or RejectCarrierInvoiceLineCommand: the line decided, its Carrier cost Invoiced if accepted"),
        ("self", "ui", "The last line decided: the invoice Approved, with the outbox message, one transaction"),
        ("msg", "ui", "pub", "outbox, within a second", dict(dashed=True)),
    ],
)

SPECS["e2-4"] = dict(
    title="E2 Carrier invoice matching \u00b7 The Carrier is told",
    subtitle="Flow carrier-invoices. The Hops as the Integration console shows them, in time order.",
    footnote=LEGEND_ARROWS + " Source of truth: docs/processes/erp/e2-carrier-invoice-matching.md, its Message trail.",
    legend=LEGEND,
    groups=[("Flow carrier-invoices", ["sub", "logic", "portal", "report"])],
    participants=[
        P("sub", "subscription carrier-invoice-outcome", "violet", "invoicing-events (topic)"),
        P("logic", "carrier-invoice-outcome", "violet", "logic-carrier-invoice-outcome"),
        P("portal", "POST /carrier/v1/carrier-id/invoice-outcomes", "gray", "partner-portal-web"),
        P("report", "POST /v1/carrier-invoices/carrier-id/number/report-told", "green", "erp-api"),
    ],
    steps=[
        ("msg", "sub", "logic", "A CarrierInvoiceApproved message is available", dict(dashed=True)),
        ("self", "logic", "Track_the_invoice, Map_the_invoice (MapCarrierInvoiceOutcome), Complete_the_message"),
        ("self", "logic", "Get_the_Carrier_endpoint_credential (Key Vault)"),
        ("msg", "logic", "portal", "Send_to_the_Carrier (the outcome of each line)"),
        ("msg", "portal", "logic", "200 acknowledged", dict(dashed=True)),
        ("msg", "logic", "report", "Report_the_outcome_to_the_ERP: Report_the_outcome_told"),
        ("msg", "report", "logic", "200 (Report_the_outcome_not_delivered instead when the retries are used up)", dict(dashed=True)),
    ],
)

SPECS["p1-1"] = dict(
    title="P1 Transport Order intake \u00b7 Message trail",
    subtitle="Flow transport-order-intake. The Hops as the Integration console shows them, in time order.",
    footnote=LEGEND_ARROWS + " Source of truth: docs/processes/tms/p1-transport-order-intake.md, its Message trail.",
    legend=LEGEND,
    groups=[("Flow transport-order-intake", ["portal", "apim", "logic", "tms", "pub"])],
    participants=[
        P("portal", "Send booking", "gray", "partner-portal-web"),
        P("apim", "Submit transport order", "violet", "apim-norvia"),
        P("logic", "transport-order-intake", "violet", "logic-intake"),
        P("tms", "POST /v1/transport-orders", "blue", "tms-api"),
        P("pub", "publish TransportOrderAccepted", "blue", "tms-api"),
    ],
    steps=[
        ("msg", "portal", "apim", "POST /partner/v1/transport-orders (partner booking)"),
        ("msg", "apim", "logic", "Canonical call, Shipper ID, correlation ID"),
        ("self", "logic", "Map_the_booking (IntakeMap)"),
        ("msg", "logic", "tms", "Submit_to_the_TMS (Canonical Model Transport Order)"),
        ("msg", "tms", "logic", "201 or 200 (repeat) or 422", dict(dashed=True)),
        ("msg", "logic", "apim", "ACCEPTED, ALREADY_BOOKED or REJECTED", dict(dashed=True)),
        ("msg", "apim", "portal", "the same answer", dict(dashed=True)),
        ("msg", "tms", "pub", "outbox, within a second", dict(dashed=True)),
    ],
)

SPECS["p11-1"] = dict(
    title="P11 Trip dispatch \u00b7 Message trail",
    subtitle="Flow trip-dispatch. The Hops as the Integration console shows them, in time order.",
    footnote=LEGEND_ARROWS + " Source of truth: docs/processes/tms/p11-trip-dispatch.md, its Message trail.",
    legend=LEGEND,
    groups=[("Flow trip-dispatch", ["ui", "call", "pub", "app", "drv", "report", "rcall"])],
    participants=[
        P("ui", "Dispatch Trip", "blue", "tms-web"),
        P("call", "POST /v1/trips/TRIP-0000007/dispatch", "blue", "tms-api"),
        P("pub", "publish TripDispatched", "blue", "tms-api"),
        P("app", "Send_to_the_Driver_app", "violet", "logic-trip-dispatch"),
        P("drv", "POST /v1/trips", "teal", "driver-app"),
        P("report", "Report_the_version_delivered", "violet", "logic-trip-dispatch"),
        P("rcall", "POST /v1/trips/TRIP-0000007/versions/1/report-delivered", "blue", "tms-api"),
    ],
    steps=[
        ("msg", "ui", "call", "command"),
        ("self", "call", "Trip rules, then the new version and the outbox message in one transaction"),
        ("msg", "call", "ui", "200, or 404, 409 or 422 and nothing changes", dict(dashed=True)),
        ("msg", "call", "pub", "outbox, within a second", dict(dashed=True)),
        ("msg", "pub", "app", "trip-events, subscription trip-dispatch", dict(dashed=True)),
        ("msg", "app", "drv", "the whole Trip, retried on a timeout, 408, 429 and 5xx"),
        ("msg", "drv", "app", "2xx, the Trip version kept", dict(dashed=True)),
        ("msg", "app", "report", "delivered"),
        ("msg", "report", "rcall", "X-Correlation-ID of the dispatch"),
        ("msg", "rcall", "report", "2xx, the version is Delivered to Driver app", dict(dashed=True)),
    ],
)

SPECS["p12-1"] = dict(
    title="P12 Trip execution \u00b7 Message trail",
    subtitle="Flow trip-execution. The Hops as the Integration console shows them, in time order.",
    footnote=LEGEND_ARROWS + " Source of truth: docs/processes/tms/p12-trip-execution.md, its Message trail.",
    legend=LEGEND,
    groups=[("Flow trip-execution", ["ui", "pub", "lapp", "call", "out", "ocall"])],
    participants=[
        P("ui", "POST /trips/TRIP-0000007/stops/SHP-0000011/collection/collected", "teal", "driver-app-web"),
        P("pub", "publish StopCompletedReported", "teal", "driver-app-web"),
        P("lapp", "Send_the_report_to_the_TMS", "blue", "logic-driver-reports"),
        P("call", "POST /v1/tracking-events", "blue", "tms-api"),
        P("out", "Report_recorded", "violet", "logic-driver-reports"),
        P("ocall", "POST /v1/report-outcomes", "teal", "driver-app-api"),
    ],
    steps=[
        ("self", "ui", "DriverReport (Sending) and the outbox message in one transaction"),
        ("msg", "ui", "pub", "outbox, within a second", dict(dashed=True)),
        ("msg", "pub", "lapp", "driver-app-events, session = Trip number, subscription driver-reports", dict(dashed=True)),
        ("msg", "lapp", "call", "Own fleet Tracking event, retried on a timeout, 408, 429 and 5xx"),
        ("self", "call", "Driver report rules, then Tracking event, Shipment status and outbox in one transaction"),
        ("msg", "call", "lapp", "2xx, or 404, 409 or 400 and nothing changes", dict(dashed=True)),
        ("msg", "lapp", "out", "message completed"),
        ("msg", "out", "ocall", "Recorded"),
        ("msg", "ocall", "out", "2xx, the report is Recorded", dict(dashed=True)),
    ],
)

SPECS["p2-1"] = dict(
    title="P2 Rating \u00b7 Message trail",
    subtitle="Flow transport-order-rating. The Hops as the Integration console shows them, in time order.",
    footnote=LEGEND_ARROWS + " Source of truth: docs/processes/tms/p2-rating.md, its Message trail.",
    legend=LEGEND,
    groups=[("Flow transport-order-rating", ["ui", "tms", "call", "pub"])],
    participants=[
        P("ui", "Re-rate Transport Order", "blue", "tms-web"),
        P("tms", "Rate Transport Order", "blue", "tms-api"),
        P("call", "POST /v1/transport-orders/TO-0000042/re-rate", "blue", "tms-api"),
        P("pub", "publish TransportOrderRated", "blue", "tms-api"),
    ],
    steps=[
        ("note", ["tms"], "First rating, after the acceptance commit or the next sweep"),
        ("self", "tms", "Rate Card version, Lane, Weight Break, Freight Charge"),
        ("msg", "tms", "pub", "outbox, within a second", dict(dashed=True)),
        ("note", ["ui"], "Re-rate, only for an order whose Rating failed"),
        ("msg", "ui", "call", "re-rate command with an Idempotency-Key"),
        ("self", "call", "the same calculation"),
        ("msg", "call", "ui", "200 with the rating result, or 409 and nothing changes", dict(dashed=True)),
        ("msg", "call", "pub", "outbox, within a second", dict(dashed=True)),
    ],
)

SPECS["p3-1"] = dict(
    title="P3 Transport planning \u00b7 Message trail",
    subtitle="Flows transport-planning and carrier-booking. The Hops as the Integration console shows them, in time order.",
    footnote=LEGEND_ARROWS + " Source of truth: docs/processes/tms/p3-transport-planning.md, its Message trail.",
    legend=LEGEND,
    groups=[("Flow transport-planning", ["ui", "call", "pub"]), ("Flow carrier-booking", ["booking"])],
    participants=[
        P("ui", "Assign Transport Order", "blue", "tms-web"),
        P("call", "POST /v1/transport-orders/TO-0000042/assign", "blue", "tms-api"),
        P("pub", "publish ShipmentPlanned", "blue", "tms-api"),
        P("booking", "carrier-booking", "violet", "logic-booking"),
    ],
    steps=[
        ("msg", "ui", "call", "command with an Idempotency-Key"),
        ("self", "call", "assignment rules, then the change and the outbox message in one transaction"),
        ("msg", "call", "ui", "201 or 200, or 404, 409 or 422 and nothing changes", dict(dashed=True)),
        ("msg", "call", "pub", "outbox, within a second", dict(dashed=True)),
        ("msg", "pub", "booking", "shipment-events, subscription carrier-booking", dict(dashed=True)),
        ("note", ["booking"], "Takes all three events of a Carrier Shipment, acts on ShipmentReleased only"),
    ],
)

SPECS["p4-1"] = dict(
    title="P4 Carrier booking \u00b7 Message trail",
    subtitle="Flows transport-planning and carrier-booking and carrier-booking-report. The Hops as the Integration console shows them, in time order.",
    footnote=LEGEND_ARROWS + " Source of truth: docs/processes/tms/p4-carrier-booking.md, its Message trail.",
    legend=LEGEND,
    groups=[("Flow transport-planning", ["ui", "rel", "pub"]), ("Flow carrier-booking", ["booking", "recv"]), ("Flow carrier-booking-report", ["sent"])],
    participants=[
        P("ui", "Release Shipment", "blue", "tms-web"),
        P("rel", "POST /v1/shipments/SH-0000042/release", "blue", "tms-api"),
        P("pub", "publish ShipmentReleased", "blue", "tms-api"),
        P("booking", "carrier-booking", "violet", "logic-booking"),
        P("recv", "POST /carrier/v1/carrier-id/bookings", "gray", "partner-portal-web"),
        P("sent", "POST /v1/carrier-bookings/CB-0000042/report-sent", "blue", "tms-api"),
    ],
    steps=[
        ("msg", "ui", "rel", "release command"),
        ("self", "rel", "Shipment Released and Carrier booking Requested, with the outbox message, in one transaction"),
        ("msg", "rel", "ui", "201 or 200", dict(dashed=True)),
        ("msg", "rel", "pub", "outbox, within a second", dict(dashed=True)),
        ("msg", "pub", "booking", "shipment-events, subscription carrier-booking", dict(dashed=True)),
        ("self", "booking", "Map_the_release, Get_the_Carrier_endpoint_credential"),
        ("msg", "booking", "recv", "Send_to_the_Carrier (Carrier booking format)"),
        ("msg", "recv", "booking", "200 received", dict(dashed=True)),
        ("msg", "booking", "sent", "Report_the_booking_sent"),
        ("msg", "sent", "booking", "200", dict(dashed=True)),
    ],
)

SPECS["p4-2"] = dict(
    title="P4 Carrier booking \u00b7 Message trail",
    subtitle="Flows carrier-answer and carrier-booking-answer. The Hops as the Integration console shows them, in time order.",
    footnote=LEGEND_ARROWS + " Source of truth: docs/processes/tms/p4-carrier-booking.md, its Message trail.",
    legend=LEGEND,
    groups=[("Flow carrier-answer", ["portal", "apim", "logic"]), ("Flow carrier-booking-answer", ["tms", "pub"])],
    participants=[
        P("portal", "Send Carrier answer", "gray", "partner-portal-web"),
        P("apim", "Answer Carrier booking", "violet", "apim-norvia"),
        P("logic", "carrier-answer", "violet", "logic-answer"),
        P("tms", "POST /v1/carrier-bookings/CB-0000042/answer", "blue", "tms-api"),
        P("pub", "publish CarrierBookingConfirmed", "blue", "tms-api"),
    ],
    steps=[
        ("msg", "portal", "apim", "POST /partner/v1/carrier-answers (Persona token and subscription key)"),
        ("msg", "apim", "logic", "Canonical call, Carrier ID from azp, correlation ID"),
        ("self", "logic", "Map_the_answer (CarrierAnswerMap)"),
        ("msg", "logic", "tms", "Answer_to_the_TMS (Canonical Model answer)"),
        ("msg", "tms", "logic", "200 (also a repeat), 404, 403, 409, 422 or 400", dict(dashed=True)),
        ("self", "logic", "Relay_the_TMS_outcome (CarrierAnswerResponses)"),
        ("msg", "logic", "apim", "RECORDED or a refusal code", dict(dashed=True)),
        ("msg", "apim", "portal", "the same answer", dict(dashed=True)),
        ("msg", "tms", "pub", "outbox, within a second", dict(dashed=True)),
    ],
)

SPECS["p5-1"] = dict(
    title="P5 Shipment tracking \u00b7 Message trail",
    subtitle="Flow carrier-tracking. The Hops as the Integration console shows them, in time order.",
    footnote=LEGEND_ARROWS + " Source of truth: docs/processes/tms/p5-shipment-tracking.md, its Message trail.",
    legend=LEGEND,
    groups=[("Flow carrier-tracking", ["portal", "apim", "queue", "logic", "tms", "pub"])],
    participants=[
        P("portal", "Send Tracking event", "gray", "partner-portal-web"),
        P("apim", "Submit Carrier tracking", "violet", "apim-norvia"),
        P("queue", "carrier-tracking (queue)", "violet", ""),
        P("logic", "carrier-tracking", "violet", "logic-tracking"),
        P("tms", "POST /v1/tracking-events", "blue", "tms-api"),
        P("pub", "publish ShipmentDelivered", "blue", "tms-api"),
    ],
    steps=[
        ("msg", "portal", "apim", "POST /partner/v1/carrier-tracking (Persona token and subscription key)"),
        ("self", "apim", "token, role Tracking.Submit, Carrier ID from azp, correlation ID"),
        ("msg", "apim", "queue", "Carrier tracking message, with correlationId, flow, partyId, sourceMessageId"),
        ("msg", "apim", "portal", "202 Accepted", dict(dashed=True)),
        ("msg", "queue", "logic", "When_a_Carrier_tracking_message_is_available", dict(dashed=True)),
        ("self", "logic", "Track_the_event, Map_the_event (CarrierTrackingMap)"),
        ("msg", "logic", "tms", "Record_in_the_TMS (Canonical Model Tracking event)"),
        ("self", "tms", "Tracking event recorded, status and ETA moved, with the outbox message, in one transaction"),
        ("msg", "tms", "logic", "200 (also a repeat), or 404, 409 or 400", dict(dashed=True)),
        ("msg", "logic", "queue", "Settle_the_message: complete, dead-letter or abandon"),
        ("msg", "tms", "pub", "outbox, within a second", dict(dashed=True)),
    ],
)

SPECS["p6-1"] = dict(
    title="P6 Proof of delivery \u00b7 Message trail",
    subtitle="Flow carrier-proof-of-delivery. The Hops as the Integration console shows them, in time order.",
    footnote=LEGEND_ARROWS + " Source of truth: docs/processes/tms/p6-proof-of-delivery.md, its Message trail.",
    legend=LEGEND,
    groups=[("Flow carrier-proof-of-delivery", ["portal", "apim", "blob", "queue", "logic", "tms", "pub"])],
    participants=[
        P("portal", "Send Proof of delivery", "gray", "partner-portal-web"),
        P("apim", "Submit Carrier Proof of delivery", "violet", "apim-norvia"),
        P("blob", "inbound-proofs-of-delivery (blob container)", "violet", ""),
        P("queue", "carrier-proof-of-delivery (queue)", "violet", ""),
        P("logic", "carrier-proof-of-delivery", "violet", "logic-proof-of-delivery"),
        P("tms", "POST /v1/proofs-of-delivery", "blue", "tms-api"),
        P("pub", "publish ProofOfDeliveryReceived", "blue", "tms-api"),
    ],
    steps=[
        ("msg", "portal", "apim", "POST /partner/v1/carrier-proofs-of-delivery (Persona token and subscription key)"),
        ("self", "apim", "token, role ProofsOfDelivery.Submit, 25 MB limit, Carrier ID from azp, correlation ID"),
        ("msg", "apim", "blob", "the body, unparsed, as a blob named for the correlation ID"),
        ("msg", "apim", "queue", "the blob's reference, with correlationId, flow, partyId, sourceMessageId"),
        ("msg", "apim", "portal", "202 Accepted", dict(dashed=True)),
        ("msg", "queue", "logic", "When_a_Carrier_Proof_of_delivery_is_available", dict(dashed=True)),
        ("msg", "logic", "blob", "Read_the_blob"),
        ("self", "logic", "Map_the_proof_of_delivery (CarrierProofOfDeliveryMap)"),
        ("msg", "logic", "tms", "Record_in_the_TMS (multipart: canonical metadata plus files)"),
        ("self", "tms", "Proof of delivery recorded as current, any earlier one superseded, Exception opened or joined, Billable decided, with the outbox message, in one transaction"),
        ("msg", "tms", "logic", "200 (also a repeat), or 404, 409 or 400", dict(dashed=True)),
        ("msg", "logic", "blob", "Delete_the_blob"),
        ("msg", "logic", "queue", "Settle_the_message: complete, dead-letter or abandon"),
        ("msg", "tms", "pub", "outbox, within a second", dict(dashed=True)),
    ],
)

SPECS["p7-1"] = dict(
    title="P7 Exception handling \u00b7 Opening",
    subtitle="Flow carrier-tracking. The Hops as the Integration console shows them, in time order.",
    footnote=LEGEND_ARROWS + " Source of truth: docs/processes/tms/p7-exception-handling.md, its Message trail.",
    legend=LEGEND,
    groups=[("Flow carrier-tracking", ["tms", "opened", "reported"])],
    participants=[
        P("tms", "POST /v1/tracking-events", "blue", "tms-api"),
        P("opened", "publish ExceptionOpened", "blue", "tms-api"),
        P("reported", "publish ShipmentExceptionReported", "blue", "tms-api"),
    ],
    steps=[
        ("self", "tms", "Tracking event recorded, Exception opened or the cause added, with the outbox messages, in one transaction"),
        ("msg", "tms", "reported", "outbox, within a second", dict(dashed=True)),
        ("msg", "tms", "opened", "outbox, within a second", dict(dashed=True)),
    ],
)

SPECS["p7-2"] = dict(
    title="P7 Exception handling \u00b7 Notifying the Shipper",
    subtitle="Flows exception-handling and shipper-notification. The Hops as the Integration console shows them, in time order.",
    footnote=LEGEND_ARROWS + " Source of truth: docs/processes/tms/p7-exception-handling.md, its Message trail.",
    legend=LEGEND,
    groups=[("Flow exception-handling", ["ui", "notify", "pub", "sent"]), ("Flow shipper-notification", ["logic", "recv"])],
    participants=[
        P("ui", "Notify Shipper", "blue", "tms-web"),
        P("notify", "POST /v1/exceptions/EX-0000042/notify-shipper", "blue", "tms-api"),
        P("pub", "publish ShipperNotificationRequested", "blue", "tms-api"),
        P("sent", "POST /v1/shipper-notifications/0192f0c1-7a2e-7c3b-9d4e-5a6b7c8d9e0f/report-sent", "blue", "tms-api"),
        P("logic", "shipper-notification", "violet", "logic-shipper-notification"),
        P("recv", "POST /shipper/v1/shipper-id/notifications", "gray", "partner-portal-web"),
    ],
    steps=[
        ("msg", "ui", "notify", "notify command (message as edited by the Planner)"),
        ("self", "notify", "Shipper notification Requested and the action recorded on the Exception, with the outbox message, in one transaction"),
        ("msg", "notify", "ui", "200", dict(dashed=True)),
        ("msg", "notify", "pub", "outbox, within a second", dict(dashed=True)),
        ("msg", "pub", "logic", "shipment-events, subscription shipper-notification", dict(dashed=True)),
        ("self", "logic", "Map_the_notification, Complete_the_message, Get_the_Shipper_endpoint_credential"),
        ("msg", "logic", "recv", "Send_to_the_Shipper (Shipper notification format)"),
        ("msg", "recv", "logic", "200 received", dict(dashed=True)),
        ("msg", "logic", "sent", "Report_the_notification_sent"),
        ("msg", "sent", "logic", "200", dict(dashed=True)),
    ],
)

SPECS["p7-3"] = dict(
    title="P7 Exception handling \u00b7 Cancelling a Carrier booking",
    subtitle="Flows exception-handling and carrier-cancellation. The Hops as the Integration console shows them, in time order.",
    footnote=LEGEND_ARROWS + " Source of truth: docs/processes/tms/p7-exception-handling.md, its Message trail.",
    legend=LEGEND,
    groups=[("Flow exception-handling", ["ui", "cancel", "pub", "told"]), ("Flow carrier-cancellation", ["logic", "recv"])],
    participants=[
        P("ui", "Cancel Carrier booking", "blue", "tms-web"),
        P("cancel", "POST /v1/exceptions/EX-0000042/cancel-carrier-booking", "blue", "tms-api"),
        P("pub", "publish CarrierBookingCancelled", "blue", "tms-api"),
        P("told", "POST /v1/carrier-bookings/CB-0000042/report-cancellation-told", "blue", "tms-api"),
        P("logic", "carrier-cancellation", "violet", "logic-carrier-cancellation"),
        P("recv", "POST /carrier/v1/carrier-id/cancellations", "gray", "partner-portal-web"),
    ],
    steps=[
        ("msg", "ui", "cancel", "cancel command (reason text)"),
        ("self", "cancel", "Carrier booking Cancelled, Shipment back to Planned and the action recorded on the Exception, with the outbox message, in one transaction"),
        ("msg", "cancel", "ui", "200", dict(dashed=True)),
        ("msg", "cancel", "pub", "outbox, within a second", dict(dashed=True)),
        ("msg", "pub", "logic", "shipment-events, subscription carrier-cancellation", dict(dashed=True)),
        ("self", "logic", "Map_the_cancellation, Complete_the_message, Get_the_Carrier_endpoint_credential"),
        ("msg", "logic", "recv", "Send_to_the_Carrier (Carrier cancellation format)"),
        ("msg", "recv", "logic", "200 received", dict(dashed=True)),
        ("msg", "logic", "told", "Report_the_cancellation_told"),
        ("msg", "told", "logic", "200", dict(dashed=True)),
    ],
)

SPECS["p8-1"] = dict(
    title="P8 Shipper visibility \u00b7 Message trail",
    subtitle="Flow shipper-tracking. The Hops as the Integration console shows them, in time order.",
    footnote=LEGEND_ARROWS + " Source of truth: docs/processes/tms/p8-shipper-visibility.md, its Message trail.",
    legend=LEGEND,
    groups=[("Flow shipper-tracking", ["portal", "apim", "tms"])],
    participants=[
        P("portal", "Get Transport Order", "gray", "partner-portal-web"),
        P("apim", "Get Transport Order", "violet", "apim-norvia"),
        P("tms", "GET /v1/shipper-tracking/transport-orders/{transportOrderNumber}", "blue", "tms-api"),
    ],
    steps=[
        ("msg", "portal", "apim", "GET /partner/v1/transport-orders/{number} (Persona token and subscription key)"),
        ("self", "apim", "token, role Tracking.Read, Shipper ID from azp, rate limit, correlation ID"),
        ("msg", "apim", "tms", "managed identity, X-Shipper-ID, X-Correlation-ID"),
        ("msg", "tms", "apim", "the Shipper's view (Canonical Model), or 404, or 5xx", dict(dashed=True)),
        ("self", "apim", "Liquid map, Canonical to Shipper tracking format"),
        ("msg", "apim", "portal", "the Shipper tracking format, or 403, 429 or 503", dict(dashed=True)),
    ],
)

SPECS["p9-1"] = dict(
    title="P9 Freight settlement \u00b7 Release with a Spot price",
    subtitle="Flows transport-planning and carrier-booking. The Hops as the Integration console shows them, in time order.",
    footnote=LEGEND_ARROWS + " Source of truth: docs/processes/tms/p9-freight-settlement.md, its Message trail.",
    legend=LEGEND,
    groups=[("Flow transport-planning", ["ui", "call", "pub"]), ("Flow carrier-booking", ["booking"])],
    participants=[
        P("ui", "Release Shipment with Spot price", "blue", "tms-web"),
        P("call", "POST /v1/shipments/SH-0000042/release-with-spot-price", "blue", "tms-api"),
        P("pub", "publish ShipmentReleased", "blue", "tms-api"),
        P("booking", "carrier-booking", "violet", "logic-booking"),
    ],
    steps=[
        ("msg", "ui", "call", "Spot price and remark, with an Idempotency-Key"),
        ("self", "call", "Carrier Rate Card can't price it, Spot price frozen on the Carrier booking, in one transaction"),
        ("msg", "call", "pub", "outbox, within a second (carries the Carrier cost)", dict(dashed=True)),
        ("msg", "pub", "booking", "shipment-events, subscription carrier-booking", dict(dashed=True)),
    ],
)

SPECS["p9-2"] = dict(
    title="P9 Freight settlement \u00b7 The Delivered Tracking event",
    subtitle="Flows carrier-tracking and billing. The Hops as the Integration console shows them, in time order.",
    footnote=LEGEND_ARROWS + " Source of truth: docs/processes/tms/p9-freight-settlement.md, its Message trail.",
    legend=LEGEND,
    groups=[("Flow carrier-tracking", ["apim", "queue", "logic", "tms", "delivered", "incurred"]), ("Flow billing", ["billable"])],
    participants=[
        P("apim", "Submit Carrier tracking", "violet", "apim-norvia"),
        P("queue", "carrier-tracking (queue)", "violet", ""),
        P("logic", "carrier-tracking", "violet", "logic-tracking"),
        P("tms", "POST /v1/tracking-events", "blue", "tms-api"),
        P("delivered", "publish ShipmentDelivered", "blue", "tms-api"),
        P("incurred", "publish CarrierCostIncurred", "blue", "tms-api"),
        P("billable", "publish TransportOrderBillable", "blue", "tms-api"),
    ],
    steps=[
        ("msg", "apim", "queue", "Carrier tracking message (POD), 202 Accepted to the Carrier"),
        ("msg", "queue", "logic", "When_a_Carrier_tracking_message_is_available", dict(dashed=True)),
        ("msg", "logic", "tms", "Record_in_the_TMS"),
        ("self", "tms", "Shipment Delivered, Carrier cost incurred, Billable decided, with the outbox messages, in one transaction"),
        ("msg", "tms", "delivered", "outbox, within a second", dict(dashed=True)),
        ("msg", "tms", "incurred", "outbox, within a second, Carrier Shipments only", dict(dashed=True)),
        ("msg", "tms", "billable", "outbox, within a second, when this made the last condition true", dict(dashed=True)),
    ],
)

SPECS["p9-3"] = dict(
    title="P9 Freight settlement \u00b7 The Proof of delivery",
    subtitle="Flows carrier-proof-of-delivery and billing. The Hops as the Integration console shows them, in time order.",
    footnote=LEGEND_ARROWS + " Source of truth: docs/processes/tms/p9-freight-settlement.md, its Message trail.",
    legend=LEGEND,
    groups=[("Flow carrier-proof-of-delivery", ["apim", "queue", "logic", "tms"]), ("Flow billing", ["billable"])],
    participants=[
        P("apim", "Submit Carrier Proof of delivery", "violet", "apim-norvia"),
        P("queue", "carrier-proof-of-delivery (queue)", "violet", ""),
        P("logic", "carrier-proof-of-delivery", "violet", "logic-proof-of-delivery"),
        P("tms", "POST /v1/proofs-of-delivery", "blue", "tms-api"),
        P("billable", "publish TransportOrderBillable", "blue", "tms-api"),
    ],
    steps=[
        ("msg", "apim", "queue", "the blob's reference, 202 Accepted to the Carrier"),
        ("msg", "queue", "logic", "When_a_Carrier_Proof_of_delivery_is_available", dict(dashed=True)),
        ("msg", "logic", "tms", "Record_in_the_TMS (multipart: canonical metadata plus files)"),
        ("self", "tms", "Proof of delivery recorded, Billable decided, with the outbox message, in one transaction"),
        ("msg", "tms", "billable", "outbox, within a second, when this made the last condition true", dict(dashed=True)),
    ],
)

SPECS["p9-4"] = dict(
    title="P9 Freight settlement \u00b7 Customer service's release",
    subtitle="Flow billing. The Hops as the Integration console shows them, in time order.",
    footnote=LEGEND_ARROWS + " Source of truth: docs/processes/tms/p9-freight-settlement.md, its Message trail.",
    legend=LEGEND,
    groups=[("Flow billing", ["ui", "call", "pub"])],
    participants=[
        P("ui", "Release without Proof of delivery", "blue", "tms-web"),
        P("call", "POST /v1/transport-orders/TO-0000042/release-without-proof-of-delivery", "blue", "tms-api"),
        P("pub", "publish TransportOrderBillable", "blue", "tms-api"),
    ],
    steps=[
        ("msg", "ui", "call", "remark and who released it"),
        ("self", "call", "release recorded, Billable decided, with the outbox message, in one transaction"),
        ("msg", "call", "pub", "outbox, within a second, when the release made the last condition true", dict(dashed=True)),
    ],
)

if __name__ == "__main__":
    render_all(SPECS, OUT, "message-trail-", sys.argv[1:])
