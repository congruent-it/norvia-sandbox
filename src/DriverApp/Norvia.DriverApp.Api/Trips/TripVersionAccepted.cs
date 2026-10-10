using Norvia.DriverApp.Application.Trips.Receive;

namespace Norvia.DriverApp.Api.Trips;

/// <summary>The acknowledgement of a Trip version: stored as the Trip's latest, or ignored because the Driver app held an equal or later one.</summary>
public sealed record TripVersionAccepted(TripVersionOutcome Outcome);
