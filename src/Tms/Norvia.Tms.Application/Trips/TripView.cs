using Norvia.Tms.Domain.Trips;

namespace Norvia.Tms.Application.Trips;

/// <param name="StopCount">How many Stops the Trip visits: two for each Shipment on it.</param>
/// <param name="LoadWeightKg">The total weight of the Trip's Shipments, which count as loaded at once.</param>
/// <param name="LoadPalletPlaces">The pallets of the Trip's Shipments.</param>
/// <param name="PayloadKg">The Vehicle's payload; null while the Trip has no (active) Vehicle.</param>
/// <param name="VehiclePalletPlaces">The Vehicle's pallet places; null while the Trip has no (active) Vehicle.</param>
/// <param name="LatestDelivery">Whether the Driver app has the Trip's latest version; null while the Trip is a Draft.</param>
public sealed record TripView(
    string Number,
    DateOnly Day,
    TripStatus Status,
    string? VehicleId,
    string? VehicleRegistration,
    string? DriverId,
    string? DriverName,
    TimeOnly? PlannedStart,
    TimeSpan? PlannedDrivingDuration,
    int StopCount = 0,
    decimal LoadWeightKg = 0m,
    int LoadPalletPlaces = 0,
    int? PayloadKg = null,
    int? VehiclePalletPlaces = null,
    TripDeliveryState? LatestDelivery = null);
