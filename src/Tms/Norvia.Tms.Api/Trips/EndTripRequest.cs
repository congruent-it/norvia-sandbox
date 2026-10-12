namespace Norvia.Tms.Api.Trips;

/// <summary>The Driver ID the Driver reports Logic App took from the Driver's token (ADR 0005), never from the Driver app's payload.</summary>
public sealed record EndTripRequest(string? DriverId);
