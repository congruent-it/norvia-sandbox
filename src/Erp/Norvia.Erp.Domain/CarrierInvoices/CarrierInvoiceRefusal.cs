using static System.FormattableString;

namespace Norvia.Erp.Domain.CarrierInvoices;

/// <summary>Why the ERP refused to receive a Carrier invoice: a stable code and a readable message.</summary>
public sealed record CarrierInvoiceRefusal(CarrierInvoiceRefusalCode Code, string Message)
{
    public static CarrierInvoiceRefusal CarrierNotFound(string carrierId) =>
        new(CarrierInvoiceRefusalCode.CarrierNotFound, Invariant($"The ERP has no Carrier {carrierId}."));

    public static CarrierInvoiceRefusal InvoiceNumberUsed(string carrierId, string invoiceNumber) =>
        new(
            CarrierInvoiceRefusalCode.InvoiceNumberUsed,
            Invariant($"Carrier {carrierId} has already used invoice number {invoiceNumber} for a Carrier invoice with different content."));

    public static CarrierInvoiceRefusal NoLines(string invoiceNumber) =>
        new(CarrierInvoiceRefusalCode.CarrierInvoiceWithoutLines, Invariant($"Carrier invoice {invoiceNumber} has no lines."));

    public static CarrierInvoiceRefusal LineWithoutReference(string invoiceNumber, int lineNumber) =>
        new(
            CarrierInvoiceRefusalCode.CarrierInvoiceLineWithoutReference,
            Invariant($"Line {lineNumber} of Carrier invoice {invoiceNumber} has no Carrier booking reference."));

    public static CarrierInvoiceRefusal LineAmountNotAboveZero(string invoiceNumber, int lineNumber, decimal amount) =>
        new(
            CarrierInvoiceRefusalCode.CarrierInvoiceLineAmountNotAboveZero,
            Invariant($"Line {lineNumber} of Carrier invoice {invoiceNumber} claims {amount}, but an amount must be above zero."));

    public static CarrierInvoiceRefusal ReferenceRepeated(string invoiceNumber, string carrierBookingReference) =>
        new(
            CarrierInvoiceRefusalCode.CarrierInvoiceReferenceRepeated,
            Invariant($"Carrier booking reference {carrierBookingReference} appears more than once in Carrier invoice {invoiceNumber}."));
}
