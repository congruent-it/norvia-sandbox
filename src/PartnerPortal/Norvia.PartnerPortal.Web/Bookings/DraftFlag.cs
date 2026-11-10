namespace Norvia.PartnerPortal.Web.Bookings;

/// <summary>A field of a Booking draft the Shipper must look at: <paramref name="Field"/> is named as the Partner API names it.</summary>
public sealed record DraftFlag(string Field, string Reason, DraftFlagKind Kind);

/// <summary>Why a field of a Booking draft is flagged.</summary>
public enum DraftFlagKind
{
    /// <summary>The text did not give a value, so the field is empty.</summary>
    Missing,

    /// <summary>The field has a value the assistant was not sure of.</summary>
    CheckThis,
}
