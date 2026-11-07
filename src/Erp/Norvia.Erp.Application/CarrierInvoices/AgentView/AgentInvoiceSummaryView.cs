namespace Norvia.Erp.Application.CarrierInvoices.AgentView;

// Like the line view, the summary is an allowlist of fields (ADR 0040): see GetAgentInvoiceSummaryHandler.

/// <summary>A Carrier invoice as a whole, as the Carrier invoice matching agent reads it (<c>get_invoice_summary</c>).</summary>
/// <param name="CarrierName">The Carrier's business name.</param>
/// <param name="Status">The invoice's status: <c>Received</c> or <c>Approved</c>.</param>
/// <param name="Total">What the Carrier claims in all, in EUR without VAT.</param>
/// <param name="ApprovedAmountSoFar">The sum of the Matched and Accepted amounts decided so far, which is the approved amount once the invoice is Approved.</param>
/// <param name="Lines">The number of lines.</param>
/// <param name="ToDecide">The number of lines that wait for Finance.</param>
/// <param name="ResolveAutomatically">The number of To decide lines that are matched again by themselves when their Carrier cost arrives.</param>
/// <param name="ByResult">The lines grouped by result, in the order Matched, Price mismatch, Unknown booking, Duplicate; a result without lines is left out.</param>
public sealed record AgentInvoiceSummaryView(
    string CarrierId,
    string CarrierName,
    string InvoiceNumber,
    DateOnly InvoiceDate,
    DateTimeOffset ReceivedAt,
    string Status,
    decimal Total,
    string Currency,
    decimal ApprovedAmountSoFar,
    int Lines,
    int ToDecide,
    int ResolveAutomatically,
    IReadOnlyList<AgentLineGroupView> ByResult);
