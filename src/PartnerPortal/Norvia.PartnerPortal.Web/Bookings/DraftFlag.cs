namespace Norvia.PartnerPortal.Web.Bookings;

/// <summary>A field of a Booking draft the Shipper must look at: <paramref name="Field"/> is named as the Partner API names it.</summary>
public sealed record DraftFlag(string Field, string Reason, DraftFlagKind Kind)
{
    /// <summary>What the form calls the flag.</summary>
    public string Title => Kind == DraftFlagKind.Missing ? "Missing" : "Check this";

    /// <summary>The words on the button that confirms it.</summary>
    public string ConfirmText => Kind == DraftFlagKind.Missing ? "Leave empty" : "Confirm";

    /// <summary>What the form says once the flag is handled.</summary>
    public string HandledText => Kind == DraftFlagKind.Missing ? "Missing, handled" : "Checked";
}

/// <summary>Why a field of a Booking draft is flagged.</summary>
public enum DraftFlagKind
{
    /// <summary>The text did not give a value, so the field is empty.</summary>
    Missing,

    /// <summary>The field is one the Intake agent was not sure of, such as two weights in the email. It may be empty or have a value.</summary>
    CheckThis,
}
