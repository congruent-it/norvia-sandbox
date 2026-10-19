namespace Norvia.Erp.Domain.CarrierInvoices;

/// <summary>What matching a Carrier invoice line against the Carrier cost of its Carrier booking reference found.</summary>
public enum CarrierInvoiceLineResult
{
    /// <summary>The Carrier cost exists for this Carrier, is Open, and the amount is within tolerance of its total.</summary>
    Matched,

    /// <summary>The Carrier cost exists for this Carrier and is Open, but the amount is outside tolerance.</summary>
    PriceMismatch,

    /// <summary>No Carrier cost with that reference has arrived for this Carrier. That includes a reference that belongs to another Carrier.</summary>
    UnknownBooking,

    /// <summary>The Carrier cost is already Invoiced by an earlier line.</summary>
    Duplicate,
}
