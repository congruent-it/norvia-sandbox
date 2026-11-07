namespace Norvia.Erp.Domain.CarrierInvoices;

/// <summary>An earlier Carrier invoice line that made a Carrier cost Invoiced, with the invoice it is on.</summary>
public sealed record EarlierLine(CarrierInvoice Invoice, CarrierInvoiceLine Line)
{
    /// <summary>
    /// The oldest Matched or Accepted line of <paramref name="invoices"/> (oldest first) for a Carrier booking number, other than the
    /// line being explained. Null when no line made it Invoiced.
    /// </summary>
    public static EarlierLine? Find(IEnumerable<CarrierInvoice> invoices, string carrierBookingNumber, string invoiceNumber, string carrierBookingReference)
    {
        ArgumentNullException.ThrowIfNull(invoices);
        return invoices
            .SelectMany(invoice => invoice.Lines.Select(line => new EarlierLine(invoice, line)))
            .FirstOrDefault(pair => pair.Line.CarrierBookingNumber == carrierBookingNumber
                && pair.Line.Status is CarrierInvoiceLineStatus.Matched or CarrierInvoiceLineStatus.Accepted
                && !(pair.Invoice.InvoiceNumber == invoiceNumber && pair.Line.CarrierBookingReference == carrierBookingReference));
    }
}
