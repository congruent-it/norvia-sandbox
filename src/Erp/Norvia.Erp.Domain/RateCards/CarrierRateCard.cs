using Ardalis.SharedKernel;
using Norvia.Erp.Domain.Carriers;

namespace Norvia.Erp.Domain.RateCards;

/// <summary>
/// The part of Norvia's agreement with a Carrier that sets what Norvia pays it for transport. It has the shape of a <see cref="RateCard"/>.
/// The ERP owns it (ADR 0013); until editing it is built (E3) it comes from the seed file with the TMS's IDs, and the ERP's UI only shows it.
/// </summary>
public sealed class CarrierRateCard : EntityBase<RateCardId>, IAggregateRoot
{
    private CarrierRateCard()
    {
    }

    public CarrierRateCard(RateCardId id, CarrierId carrierId, IReadOnlyList<RateCardVersion> versions)
    {
        ArgumentNullException.ThrowIfNull(versions);
        Id = id;
        CarrierId = carrierId;
        Versions = versions;
    }

    public CarrierId CarrierId { get; private set; }

    public IReadOnlyList<RateCardVersion> Versions { get; private set; } = [];

    /// <summary>Takes over what the seed file says now.</summary>
    public void Reload(CarrierId carrierId, IReadOnlyList<RateCardVersion> versions)
    {
        ArgumentNullException.ThrowIfNull(versions);
        CarrierId = carrierId;
        Versions = versions;
    }
}
