namespace Norvia.Simulator.Domain.Drivers;

/// <summary>What becomes of one Shipment on a Trip that runs today: whether its collection or delivery fails, whether the Driver reports damage on it, and whether it is delivered with reservations.</summary>
public sealed record DriverJourney(int Index, bool CollectionFailed, bool DeliveryFailed, bool Damaged, bool Reservations)
{
    public bool Delivered => !CollectionFailed && !DeliveryFailed;
}
