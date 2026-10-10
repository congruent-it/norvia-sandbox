namespace Norvia.Tms.Application.Trips.GetByNumber;

/// <summary>A rule a Trip breaks: a stable code and a readable message.</summary>
public sealed record TripRuleFailureView(string Code, string Message);
