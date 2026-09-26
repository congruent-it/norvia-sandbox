namespace Norvia.Tms.Domain.TransportOrders;

/// <summary>Why the Rate Card couldn't price a Transport Order: a stable code and a plain-language message.</summary>
public sealed record RatingFailure(RatingFailureCode Code, string Message);
