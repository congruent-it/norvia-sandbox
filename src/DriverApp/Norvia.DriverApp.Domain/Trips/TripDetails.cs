namespace Norvia.DriverApp.Domain.Trips;

/// <summary>Everything a Trip version says except which Trip and version it is: the part the Driver app shows and the latest version replaces whole (ADR 0015).</summary>
/// <param name="Day">The day of the Trip, in Norvia's local time.</param>
/// <param name="PlannedStart">The time on the Trip's day the Trip starts, in Norvia's local time.</param>
/// <param name="Stops">The Stops in the order the Driver visits them. A Shipment taken off appears only as a version without its Stops.</param>
public sealed record TripDetails(
    DateOnly Day,
    TimeOnly PlannedStart,
    TimeSpan PlannedDrivingDuration,
    TripVehicle Vehicle,
    string DriverName,
    string CorrelationId,
    IReadOnlyList<TripStop> Stops);

public sealed record TripVehicle(string VehicleId, string Registration, string VehicleType);

public enum StopKind
{
    Collection,
    Delivery,
}

public sealed record TripStop(
    int Sequence,
    StopKind Kind,
    string ShipmentNumber,
    string TransportOrderNumber,
    string ShipperId,
    StopAddress? Address,
    StopWindow? Window,
    IReadOnlyList<StopGoodsLine> GoodsLines,
    StopConsignee? Consignee);

public sealed record StopAddress(string? Street, string? Postcode, string? City, string? Country);

public sealed record StopWindow(DateTimeOffset? Earliest, DateTimeOffset? Latest);

public sealed record StopGoodsLine(string? PackageType, int? Quantity, decimal? WeightKg, StopDimensions? DimensionsCm);

public sealed record StopDimensions(decimal? Length, decimal? Width, decimal? Height);

public sealed record StopConsignee(string? Name, string? ContactName, string? Phone, string? Email);
