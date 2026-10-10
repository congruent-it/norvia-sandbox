namespace Norvia.Tms.Api.Trips;

/// <summary>The Trip dispatch Logic App's report that a Trip version never reached the Driver app.</summary>
/// <param name="Cause">The technical cause in plain language. Required.</param>
public sealed record TripNotDeliveredRequest(string? Cause);
