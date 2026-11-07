namespace Norvia.Erp.Application.Agents.RejectionReason;

/// <summary>
/// What the Carrier itself can see of one of its Carrier invoice lines, as the rejection reason agent reads it
/// (<c>get_carrier_visible_line_facts</c>). The reason leaves Norvia in the invoice outcome (E2), so the view is an allowlist of fields
/// (ADR 0040, #473): no Rate Card or version, no source of the price, no Planner remark, no tolerance, no other Carrier's reference and
/// no personal data. The Carrier-visible-facts test holds the list.
/// </summary>
/// <param name="CarrierId">The Carrier's own ID.</param>
/// <param name="InvoiceNumber">The Carrier's own invoice number.</param>
/// <param name="CarrierBookingReference">The reference the Carrier gave the line.</param>
/// <param name="Result">What matching found: <c>PriceMismatch</c>, <c>UnknownBooking</c>, <c>Duplicate</c> or <c>Matched</c>.</param>
/// <param name="InvoicedAmount">What the Carrier claims for the line, in EUR without VAT.</param>
/// <param name="AgreedCarrierCost">The Carrier cost total the Carrier accepted when it confirmed the booking; null for an Unknown booking.</param>
/// <param name="LikelyCorrectReference">For an Unknown booking: the reference of the Carrier's own Open booking the ERP ranks best, never another Carrier's. Null when none looks likely.</param>
/// <param name="EarlierInvoiceNumber">For a Duplicate: the Carrier's own earlier invoice that Invoiced the Carrier cost; null otherwise.</param>
public sealed record CarrierVisibleLineFactsView(
    string CarrierId,
    string InvoiceNumber,
    string CarrierBookingReference,
    string Result,
    decimal InvoicedAmount,
    string Currency,
    decimal? AgreedCarrierCost,
    string? LikelyCorrectReference,
    string? EarlierInvoiceNumber);
