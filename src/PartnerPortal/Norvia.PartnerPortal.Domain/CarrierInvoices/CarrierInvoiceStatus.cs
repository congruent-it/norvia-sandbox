namespace Norvia.PartnerPortal.Domain.CarrierInvoices;

/// <summary>Where a Carrier invoice stands at Norvia, as it last told the Carrier (E2).</summary>
public enum CarrierInvoiceStatus
{
    /// <summary>Norvia recorded it, and a line waits for Finance.</summary>
    Received,

    /// <summary>Every line is decided. Norvia tells the outcome through the Carrier's invoice outcome endpoint.</summary>
    Approved,
}
