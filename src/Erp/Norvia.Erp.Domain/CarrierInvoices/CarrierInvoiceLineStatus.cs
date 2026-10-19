namespace Norvia.Erp.Domain.CarrierInvoices;

public enum CarrierInvoiceLineStatus
{
    /// <summary>Accepted automatically at the invoiced amount. Final.</summary>
    Matched,

    /// <summary>A Price mismatch, Unknown booking or Duplicate line, which waits for Finance.</summary>
    ToDecide,
}
