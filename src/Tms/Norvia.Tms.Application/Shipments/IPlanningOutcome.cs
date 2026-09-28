namespace Norvia.Tms.Application.Shipments;

/// <summary>What a Planner's action on a Shipment answers with, as its trace needs it: the Shipment, its Transport Order and Shipper, and the action's trail.</summary>
public interface IPlanningOutcome
{
    string ShipmentNumber { get; }

    string TransportOrderNumber { get; }

    string ShipperId { get; }

    CarriedByView CarriedBy { get; }

    /// <summary>The action's own Message trail; for a repeat, the trail of the action it repeats.</summary>
    string CorrelationId { get; }

    /// <summary>True when the action was sent again under its key, and answered as before without acting again.</summary>
    bool Repeat { get; }
}
