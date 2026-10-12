using Norvia.DriverApp.Domain.Trips;

namespace Norvia.DriverApp.Application.Trips;

/// <summary>Maps between the DTOs the Application's callers use and the Domain's value objects, so no Domain type crosses the boundary outwards.</summary>
internal static class TripMapping
{
    private const string DispatchedStatus = "Dispatched";

    public static TripStop ToDomain(this StopDto stop) => new(
        stop.Sequence,
        stop.Kind == StopKindDto.Collection ? StopKind.Collection : StopKind.Delivery,
        stop.ShipmentNumber,
        stop.TransportOrderNumber,
        stop.ShipperId,
        stop.Address is { } address ? new StopAddress(address.Street, address.Postcode, address.City, address.Country) : null,
        stop.Window is { } window ? new StopWindow(window.Earliest, window.Latest) : null,
        [.. stop.GoodsLines.Select(line => new StopGoodsLine(
            line.PackageType, line.Quantity, line.WeightKg,
            line.DimensionsCm is { } size ? new StopDimensions(size.Length, size.Width, size.Height) : null))],
        stop.Consignee is { } consignee ? new StopConsignee(consignee.Name, consignee.ContactName, consignee.Phone, consignee.Email) : null);

    public static StopDto ToDto(this TripStop stop) => new(
        stop.Sequence,
        stop.Kind == StopKind.Collection ? StopKindDto.Collection : StopKindDto.Delivery,
        stop.ShipmentNumber,
        stop.TransportOrderNumber,
        stop.ShipperId,
        stop.Address is { } address ? new StopAddressDto(address.Street, address.Postcode, address.City, address.Country) : null,
        stop.Window is { } window ? new StopWindowDto(window.Earliest, window.Latest) : null,
        [.. stop.GoodsLines.Select(line => new StopGoodsLineDto(
            line.PackageType, line.Quantity, line.WeightKg,
            line.DimensionsCm is { } size ? new StopDimensionsDto(size.Length, size.Width, size.Height) : null))],
        stop.Consignee is { } consignee ? new StopConsigneeDto(consignee.Name, consignee.ContactName, consignee.Phone, consignee.Email) : null);

    public static DriverTripView ToView(this DriverTripRow row) => new(
        row.TripNumber.Value,
        row.Version,
        DispatchedStatus,
        row.DriverId.Value,
        row.Details.DriverName,
        row.Details.Day,
        row.Details.PlannedStart,
        row.Details.PlannedDrivingDuration,
        new TripVehicleDto(row.Details.Vehicle.VehicleId, row.Details.Vehicle.Registration, row.Details.Vehicle.VehicleType),
        [.. row.Details.Stops.Select(stop => stop.ToDto())]);
}
