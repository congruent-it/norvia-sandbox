using Norvia.Tms.Domain.Trips;

namespace Norvia.Tms.Application.Trips;

public sealed record TripView(string Number, DateOnly Day, TripStatus Status, string? VehicleId, string? VehicleRegistration, string? DriverId, string? DriverName);
