namespace Norvia.Erp.Application.CarrierInvoices.AgentView;

// The Finance agent's view is an allowlist of fields (ADR 0040): see GetAgentInvoiceLineHandler.

/// <summary>A Carrier invoice line as the Carrier invoice matching agent reads it (<c>get_invoice_line</c>).</summary>
/// <param name="CarrierName">The Carrier's business name.</param>
/// <param name="Result">What matching found: <c>PriceMismatch</c>, <c>UnknownBooking</c>, <c>Duplicate</c> or <c>Matched</c>.</param>
/// <param name="Status">The line's status: <c>ToDecide</c>, <c>Matched</c>, <c>Accepted</c> or <c>Rejected</c>.</param>
/// <param name="InvoicedAmount">What the Carrier claims for the line, in EUR without VAT.</param>
/// <param name="Tolerance">The Matching tolerance in EUR: how far the invoiced amount may be from the agreed total and still match.</param>
/// <param name="Difference">The invoiced amount minus the agreed Carrier cost total (positive: the Carrier claims more); null when the line has no Carrier cost.</param>
/// <param name="CarrierCost">The agreed Carrier cost the line was matched against; null for an Unknown booking.</param>
/// <param name="InvoicedBy">For a Duplicate: the earlier invoice and line that Invoiced the Carrier cost; null otherwise.</param>
/// <param name="Candidates">For an Unknown booking: the ERP's ranked, likely candidates among the Carrier's Open Carrier costs, best first. Empty when none looks likely.</param>
/// <param name="AllowedActions">What the ERP allows Finance to do with the line, by wire name: <c>accept</c>, <c>accept-adjusted</c>, <c>reject</c> and <c>wait</c>. A Suggestion may name only one of these.</param>
/// <param name="ResolvesAutomatically">Whether the line is re-matched by itself when its Carrier cost arrives: true for an Unknown booking line that waits for Finance.</param>
public sealed record AgentInvoiceLineView(
    string CarrierId,
    string CarrierName,
    string InvoiceNumber,
    DateOnly InvoiceDate,
    string CarrierBookingReference,
    string Result,
    string Status,
    decimal InvoicedAmount,
    string Currency,
    decimal Tolerance,
    decimal? Difference,
    AgentCarrierCostView? CarrierCost,
    AgentEarlierLineView? InvoicedBy,
    IReadOnlyList<AgentCandidateView> Candidates,
    bool ResolvesAutomatically,
    IReadOnlyList<string> AllowedActions);
