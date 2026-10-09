using Norvia.Tms.Application.TransportOrders;

namespace Norvia.Tms.Application.Shipments.ListToRelease;

/// <summary>A Planned Shipment in the Planner's list of Shipments to release, with who carries it.</summary>
/// <param name="LastCarrierBookingOutcome">
/// The last Carrier booking's outcome, for a Shipment that came back from one; null for a Shipment never released.
/// </param>
/// <param name="PricingFailure">
/// Why the Carrier Rate Card can't price the Shipment, so releasing it would fail and only a Spot price releases it (P9);
/// null for Own fleet and for a Shipment the Carrier Rate Card prices.
/// </param>
public sealed record ShipmentToReleaseSummary(
    string ShipmentNumber,
    string TransportOrderNumber,
    ShipperView Shipper,
    CarriedByView CarriedBy,
    DateTimeOffset PlannedAt,
    LastCarrierBookingOutcome? LastCarrierBookingOutcome = null,
    PricingFailureView? PricingFailure = null);
