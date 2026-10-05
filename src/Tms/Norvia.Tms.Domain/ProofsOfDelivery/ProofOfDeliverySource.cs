using Norvia.Tms.Domain.Carriers;

namespace Norvia.Tms.Domain.ProofsOfDelivery;

/// <summary>
/// Who sent a Proof of delivery: one named Carrier, or Norvia's Own fleet (a Driver, through P12). Naming the source in the
/// input is what lets P12 add a second caller without changing the module.
/// </summary>
public sealed record ProofOfDeliverySource
{
    public static readonly ProofOfDeliverySource OwnFleet = new(carrierId: null);

    private ProofOfDeliverySource(CarrierId? carrierId) => CarrierId = carrierId;

    /// <summary>The Carrier that sent the Proof of delivery; null for Own fleet.</summary>
    public CarrierId? CarrierId { get; }

    public bool IsOwnFleet => CarrierId is null;

    public static ProofOfDeliverySource Carrier(CarrierId carrierId)
    {
        ArgumentNullException.ThrowIfNull(carrierId);
        return new(carrierId);
    }

    public override string ToString() => CarrierId?.Value ?? "Own fleet";
}
