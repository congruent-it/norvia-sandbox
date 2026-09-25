namespace Norvia.PartnerPortal.Domain.Bookings;

/// <summary>One rule a booking broke, with Norvia's stable code and its plain-language text.</summary>
public sealed record RejectionReason(string Code, string Text);
