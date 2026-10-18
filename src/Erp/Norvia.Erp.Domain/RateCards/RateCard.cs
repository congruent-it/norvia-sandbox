using Ardalis.SharedKernel;
using Norvia.Erp.Domain.Shippers;

namespace Norvia.Erp.Domain.RateCards;

/// <summary>
/// The part of a Shipper's Contract that sets what Norvia charges for transport, by Lane and weight. The ERP owns it (ADR 0006); until
/// editing it is built (E3) it comes from the seed file with the TMS's IDs, and the ERP's UI only shows it.
/// </summary>
public sealed class RateCard : EntityBase<RateCardId>, IAggregateRoot
{
    private RateCard()
    {
    }

    public RateCard(RateCardId id, ShipperId shipperId, IReadOnlyList<RateCardVersion> versions)
    {
        ArgumentNullException.ThrowIfNull(versions);
        Id = id;
        ShipperId = shipperId;
        Versions = versions;
    }

    public ShipperId ShipperId { get; private set; }

    public IReadOnlyList<RateCardVersion> Versions { get; private set; } = [];

    /// <summary>Takes over what the seed file says now.</summary>
    public void Reload(ShipperId shipperId, IReadOnlyList<RateCardVersion> versions)
    {
        ArgumentNullException.ThrowIfNull(versions);
        ShipperId = shipperId;
        Versions = versions;
    }
}
