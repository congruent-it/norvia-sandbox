namespace Norvia.Erp.Application.CarrierInvoices.AgentView;

/// <summary>The lines of a Carrier invoice that share one result (<c>get_invoice_summary</c>).</summary>
/// <param name="Result">What matching found: <c>Matched</c>, <c>PriceMismatch</c>, <c>UnknownBooking</c> or <c>Duplicate</c>.</param>
/// <param name="Lines">How many lines have this result.</param>
/// <param name="ToDecide">How many of them still wait for Finance.</param>
/// <param name="Amount">The sum of what the Carrier claims for them, in EUR without VAT.</param>
/// <param name="ResolveAutomatically">How many of the To decide lines are matched again by themselves when their Carrier cost arrives: only an Unknown booking line can be.</param>
public sealed record AgentLineGroupView(string Result, int Lines, int ToDecide, decimal Amount, int ResolveAutomatically);
