namespace Norvia.PartnerPortal.Domain.ShipperNotifications;

/// <summary>
/// When the goods are now due, local to the delivery address: the current ETA, or the agreed delivery window, which
/// replaces it (P7). It can also say neither. It is never both.
/// </summary>
public sealed record DeliveryTiming
{
    public static readonly DeliveryTiming None = new(null, null, null);

    // EF Core binds its columns to these parameters; the factories below keep the combinations to the three the format allows.
    private DeliveryTiming(DateTime? eta, DateTime? deliverFrom, DateTime? deliverUntil)
    {
        Eta = eta;
        DeliverFrom = deliverFrom;
        DeliverUntil = deliverUntil;
    }

    public DateTime? Eta { get; }

    public DateTime? DeliverFrom { get; }

    public DateTime? DeliverUntil { get; }

    public bool HasWindow => DeliverFrom is not null;

    public static DeliveryTiming WithEta(DateTime eta) => new(eta, null, null);

    public static DeliveryTiming WithWindow(DateTime deliverFrom, DateTime deliverUntil) => new(null, deliverFrom, deliverUntil);
}
