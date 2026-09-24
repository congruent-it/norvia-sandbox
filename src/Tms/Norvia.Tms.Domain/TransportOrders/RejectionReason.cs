namespace Norvia.Tms.Domain.TransportOrders;

/// <summary>One Transport Order rule a booking broke: its stable code and a plain-language message.</summary>
public sealed record RejectionReason(RejectionCode Code, string Message);
