namespace Norvia.Erp.Domain.CarrierInvoices;

/// <summary>What Finance, or an Agent's Suggestion, may do with a Carrier invoice line. See <see cref="CarrierInvoice.AllowedActionsOf"/>.</summary>
[Flags]
public enum CarrierInvoiceLineActions
{
    None = 0,

    /// <summary>Accept at the invoiced amount.</summary>
    Accept = 1,

    /// <summary>Accept at an adjusted amount, with a remark.</summary>
    AcceptAdjusted = 2,

    /// <summary>Reject with a reason.</summary>
    Reject = 4,

    /// <summary>Leave the line: a late Carrier cost matches it again by itself.</summary>
    Wait = 8,
}
