using Norvia.Tms.Application.Fleet.Planning;
using Norvia.Tms.Application.Shipments;
using Norvia.Tms.Domain.Shipments;

namespace Norvia.Tms.Application.TransportOrders.Assign;

/// <summary>The Shipment an assignment planned, with the Shipper and the assignment's own correlation ID for its trail.</summary>
/// <param name="Repeat">True when the assignment was sent again under its key, and answered as before without planning.</param>
/// <param name="LoadWarning">For an Own fleet assignment, the load check warning for the collection day including this Shipment; null when there is none. Never blocks.</param>
public sealed record AssignTransportOrderResult(
    string ShipmentNumber,
    string TransportOrderNumber,
    ShipmentStatus Status,
    CarriedByView CarriedBy,
    string ShipperId,
    string CorrelationId,
    bool Repeat = false,
    LoadCheckWarning? LoadWarning = null) : IPlanningOutcome;
