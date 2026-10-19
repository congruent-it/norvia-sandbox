namespace Norvia.Erp.Domain.CarrierInvoices;

public enum CarrierInvoiceLineStatus
{
    /// <summary>Accepted automatically at the invoiced amount. Final.</summary>
    Matched,

    /// <summary>A Price mismatch, Unknown booking or Duplicate line, which waits for Finance.</summary>
    ToDecide,

    /// <summary>Finance accepted it, at the invoiced amount or an adjusted amount, with a remark when adjusted. Its Carrier cost is Invoiced. Final.</summary>
    Accepted,

    /// <summary>Finance rejected it, with a reason. Its Carrier cost, if any, stays as it was. Final.</summary>
    Rejected,
}
