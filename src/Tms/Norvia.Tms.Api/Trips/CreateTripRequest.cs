namespace Norvia.Tms.Api.Trips;

public sealed record CreateTripRequest(DateOnly? Day, string? VehicleId, string? DriverId);
