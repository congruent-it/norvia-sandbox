namespace Norvia.Tms.Domain.Shipments;

public readonly record struct ShipmentId(Guid Value)
{
    public static ShipmentId New() => new(Guid.CreateVersion7());
}
