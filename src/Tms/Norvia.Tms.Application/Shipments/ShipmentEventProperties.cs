namespace Norvia.Tms.Application.Shipments;

/// <summary>The message properties every Shipment event carries for subscriptions to filter on.</summary>
public static class ShipmentEventProperties
{
    /// <summary>Whether Own fleet or a Carrier carries the Shipment: <c>OwnFleet</c> or <c>Carrier</c>.</summary>
    public const string AssignedTo = "assignedTo";
}
