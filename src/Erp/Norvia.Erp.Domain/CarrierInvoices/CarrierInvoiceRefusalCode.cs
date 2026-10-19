namespace Norvia.Erp.Domain.CarrierInvoices;

/// <summary>The stable codes of the receiving and deciding rules. The names are part of the ERP billing API's contract.</summary>
public enum CarrierInvoiceRefusalCode
{
    CarrierNotFound,
    InvoiceNumberUsed,
    CarrierInvoiceWithoutLines,
    CarrierInvoiceLineWithoutReference,
    CarrierInvoiceLineAmountNotAboveZero,
    CarrierInvoiceReferenceRepeated,
    CarrierInvoiceNotFound,
    CarrierInvoiceLineNotFound,
    CarrierInvoiceLineAlreadyDecided,
    CarrierInvoiceLineHasNoOpenCarrierCost,
    CarrierInvoiceAdjustedAmountNotAboveZero,
    CarrierInvoiceAdjustedAmountNeedsRemark,
    CarrierInvoiceRejectionNeedsReason,
}
