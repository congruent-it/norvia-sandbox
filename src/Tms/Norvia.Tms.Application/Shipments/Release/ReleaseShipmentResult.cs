using Norvia.Tms.Domain.Shipments;

namespace Norvia.Tms.Application.Shipments.Release;

/// <summary>The Shipment a release handed over: who carries it, with the Shipper and the release's own correlation ID for its trail.</summary>
/// <param name="Repeat">True when the release was sent again under its key, and answered as before without releasing.</param>
public sealed record ReleaseShipmentResult(
    string ShipmentNumber,
    string TransportOrderNumber,
    ShipmentStatus Status,
    CarriedByView CarriedBy,
    string ShipperId,
    string CorrelationId,
    bool Repeat = false) : IPlanningOutcome;
