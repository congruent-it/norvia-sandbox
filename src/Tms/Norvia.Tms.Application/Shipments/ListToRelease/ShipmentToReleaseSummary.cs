using Norvia.Tms.Application.TransportOrders;

namespace Norvia.Tms.Application.Shipments.ListToRelease;

/// <summary>A Planned Shipment in the Planner's list of Shipments to release, with who carries it.</summary>
public sealed record ShipmentToReleaseSummary(
    string ShipmentNumber,
    string TransportOrderNumber,
    ShipperView Shipper,
    CarriedByView CarriedBy,
    DateTimeOffset PlannedAt);
