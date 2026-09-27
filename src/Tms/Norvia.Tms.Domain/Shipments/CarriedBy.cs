using Norvia.Tms.Domain.Carriers;

namespace Norvia.Tms.Domain.Shipments;

/// <summary>Who carries a Shipment: Norvia's Own fleet as a whole, or one named Carrier.</summary>
public sealed record CarriedBy
{
    public static readonly CarriedBy OwnFleet = new(carrierId: null);

    private CarriedBy(CarrierId? carrierId) => CarrierId = carrierId;

    /// <summary>The Carrier that carries the Shipment; null for Own fleet.</summary>
    public CarrierId? CarrierId { get; }

    public bool IsOwnFleet => CarrierId is null;

    public static CarriedBy Carrier(CarrierId carrierId)
    {
        ArgumentNullException.ThrowIfNull(carrierId);
        return new(carrierId);
    }

    public override string ToString() => CarrierId?.Value ?? "Own fleet";
}
