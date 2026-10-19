namespace Norvia.PartnerPortal.Domain.CarrierInvoices;

/// <summary>Which of a Carrier Persona's bookings it has invoiced already.</summary>
public static class CarrierInvoiced
{
    /// <summary>
    /// The booking references the invoices claim, except those Norvia told the Carrier it rejected. A line still waiting
    /// for Finance counts as invoiced, so the Carrier doesn't claim it twice; a rejected one is free to claim again, with
    /// a new invoice number.
    /// </summary>
    public static IReadOnlySet<string> References(IEnumerable<CarrierInvoice> invoices, IEnumerable<CarrierInvoiceOutcome> outcomes)
    {
        ArgumentNullException.ThrowIfNull(invoices);
        ArgumentNullException.ThrowIfNull(outcomes);
        var told = outcomes.ToLookup(outcome => outcome.InvoiceNumber);
        return invoices
            .SelectMany(invoice => invoice.Lines
                .Where(line => !told[invoice.InvoiceNumber].Any(outcome => outcome.Rejects(line.BookingRef)))
                .Select(line => line.BookingRef))
            .ToHashSet(StringComparer.Ordinal);
    }
}
