namespace Norvia.Erp.Domain.CreditNotes;

/// <summary>The stable codes of a refused Credit note. The names are part of the ERP's contract.</summary>
public enum CreditNoteRefusalCode
{
    /// <summary>The ERP has no Shipper invoice with the number given.</summary>
    ShipperInvoiceNotFound,

    /// <summary>The invoice has no line for the Transport Order given.</summary>
    InvoiceLineNotFound,

    /// <summary>A Credit note needs a reason.</summary>
    CreditNoteReasonRequired,

    /// <summary>The reason is longer than <see cref="CreditNote.MaxReasonLength"/> characters.</summary>
    CreditNoteReasonTooLong,

    /// <summary>The amount isn't above zero, or has more than cents.</summary>
    CreditNoteAmountInvalid,

    /// <summary>The amount is above the line's total minus the earlier Credit notes on that line.</summary>
    CreditNoteAboveLineTotal,

    /// <summary>The ERP has no Credit note with the number given, for a report or Send again.</summary>
    CreditNoteNotFound,

    /// <summary>Send again was chosen for a Credit note that isn't Not delivered.</summary>
    CreditNoteNotSendableAgain,
}
