namespace Norvia.Erp.Domain.CarrierInvoices;

public enum CarrierInvoiceStatus
{
    /// <summary>Recorded, with at least one line To decide.</summary>
    Received,

    /// <summary>Every line is Matched, Accepted or Rejected. The approved amount is the sum of the Matched and Accepted amounts. Final.</summary>
    Approved,
}
