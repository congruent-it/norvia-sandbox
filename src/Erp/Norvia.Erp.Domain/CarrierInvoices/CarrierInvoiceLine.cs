namespace Norvia.Erp.Domain.CarrierInvoices;

/// <summary>One line of a Carrier invoice: what the Carrier claims for one Carrier booking, and what matching it found.</summary>
/// <param name="CarrierBookingReference">The Carrier's own reference for the booking, as the Carrier invoice quotes it.</param>
/// <param name="Amount">What the Carrier claims, in EUR without VAT.</param>
/// <param name="CarrierBookingNumber">The Carrier booking number of the Carrier cost the line was matched against; null for an Unknown booking.</param>
/// <param name="DecidedAmount">What Finance accepted the line at: the invoiced amount or an adjusted one; null unless the line is Accepted.</param>
/// <param name="Remark">Finance's remark on an adjusted amount, or its reason for a rejection.</param>
public sealed record CarrierInvoiceLine(
    string CarrierBookingReference,
    decimal Amount,
    string? Description,
    CarrierInvoiceLineResult Result,
    CarrierInvoiceLineStatus Status,
    string? CarrierBookingNumber,
    decimal? DecidedAmount = null,
    string? Remark = null)
{
    /// <summary>What the line contributes to the approved amount: its amount once Matched, Finance's amount once Accepted, nothing otherwise.</summary>
    public decimal? AcceptedAmount => Status switch
    {
        CarrierInvoiceLineStatus.Matched => Amount,
        CarrierInvoiceLineStatus.Accepted => DecidedAmount ?? Amount,
        _ => null,
    };
}
