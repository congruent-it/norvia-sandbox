namespace Norvia.Erp.Domain.CarrierInvoices;

/// <summary>One line of a Carrier invoice: what the Carrier claims for one Carrier booking, and what matching it found.</summary>
/// <param name="CarrierBookingReference">The Carrier's own reference for the booking, as the Carrier invoice quotes it.</param>
/// <param name="Amount">What the Carrier claims, in EUR without VAT.</param>
/// <param name="CarrierBookingNumber">The Carrier booking number of the Carrier cost the line was matched against; null for an Unknown booking.</param>
public sealed record CarrierInvoiceLine(
    string CarrierBookingReference,
    decimal Amount,
    string? Description,
    CarrierInvoiceLineResult Result,
    CarrierInvoiceLineStatus Status,
    string? CarrierBookingNumber)
{
    /// <summary>What the line contributes to the approved amount: its amount once Matched, nothing while it waits for Finance.</summary>
    public decimal? AcceptedAmount => Status == CarrierInvoiceLineStatus.Matched ? Amount : null;
}
