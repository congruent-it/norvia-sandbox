namespace Norvia.PartnerPortal.Application.Bookings.DraftFromText;

/// <summary>A field of the draft the Shipper must look at, with the reason. <paramref name="Field"/> is named as the Partner API names it.</summary>
public sealed record FlaggedField(string Field, string Reason);
