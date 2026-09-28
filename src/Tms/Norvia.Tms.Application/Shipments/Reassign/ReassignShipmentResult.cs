using Norvia.Tms.Domain.Shipments;

namespace Norvia.Tms.Application.Shipments.Reassign;

/// <summary>
/// The Shipment a reassignment changed: who carries it now and who carried it before, with the Shipper and the
/// reassignment's own correlation ID for its trail.
/// </summary>
/// <param name="Repeat">True when the reassignment was sent again under its key, and answered as before without reassigning.</param>
public sealed record ReassignShipmentResult(
    string ShipmentNumber,
    string TransportOrderNumber,
    ShipmentStatus Status,
    CarriedByView CarriedBy,
    CarriedByView PreviouslyCarriedBy,
    string ShipperId,
    string CorrelationId,
    bool Repeat = false) : IPlanningOutcome;
