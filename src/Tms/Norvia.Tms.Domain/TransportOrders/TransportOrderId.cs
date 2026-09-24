namespace Norvia.Tms.Domain.TransportOrders;

public readonly record struct TransportOrderId(Guid Value)
{
    public static TransportOrderId New() => new(Guid.CreateVersion7());
}
