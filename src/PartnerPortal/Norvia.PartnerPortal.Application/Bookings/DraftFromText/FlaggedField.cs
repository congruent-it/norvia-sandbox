namespace Norvia.PartnerPortal.Application.Bookings.DraftFromText;

/// <summary>A field of the draft the Shipper must look at, with the reason. <paramref name="CheckThis"/> is a field the agent was not sure of; any other flagged field is one the text did not give. <paramref name="Field"/> is named as the Partner API names it.</summary>
public sealed record FlaggedField(string Field, string Reason, bool CheckThis = false);
