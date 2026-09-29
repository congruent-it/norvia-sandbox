using Norvia.Tms.Application.TransportOrders;

namespace Norvia.Tms.Application.Shipments.ListToRelease;

/// <summary>A Planned Shipment in the Planner's list of Shipments to release, with who carries it.</summary>
/// <param name="LastCarrierBookingOutcome">
/// The last Carrier booking's outcome, for a Shipment that came back from one; null for a Shipment never released.
/// </param>
public sealed record ShipmentToReleaseSummary(
    string ShipmentNumber,
    string TransportOrderNumber,
    ShipperView Shipper,
    CarriedByView CarriedBy,
    DateTimeOffset PlannedAt,
    LastCarrierBookingOutcome? LastCarrierBookingOutcome = null);
