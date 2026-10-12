namespace Norvia.Tms.Api.Trips;

/// <param name="DriverId">The Driver ID the Driver reports Logic App took from the Driver's token (ADR 0005), never from the Driver app's payload.</param>
/// <param name="DriverAppEventId">The ID of the Driver app event being recorded. A repeat of it, as the Logic App's retry sends, is answered as a repeat.</param>
public sealed record StartTripRequest(string? DriverId, string? DriverAppEventId = null);
