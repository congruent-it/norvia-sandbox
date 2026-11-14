using Ardalis.SharedKernel;
using Norvia.Erp.Domain.Carriers;

namespace Norvia.Erp.Domain.RateCards;

/// <summary>
/// The part of Norvia's agreement with a Carrier that sets what Norvia pays it for transport. It has the shape of a <see cref="RateCard"/>.
/// The ERP owns it (ADR 0013); it starts from the seed file with the TMS's IDs, at
/// Master-data revision 1, and Finance edits it from there; each edit raises the revision and is replicated to the TMS (E3).
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

    /// <summary>The Master-data revision: 1 as loaded from the seed file, raised by every edit.</summary>
    public int Revision { get; private set; } = 1;

    /// <summary>True once Finance created or edited the card, so the seed file no longer speaks for it.</summary>
    public bool FinanceOwned { get; private set; }

    /// <summary>A card Finance creates: revision 1, and the seed file never reloads it. Its versions must satisfy <see cref="RateCardVersionRules"/>.</summary>
    public static CarrierRateCard CreateByFinance(RateCardId id, CarrierId carrierId, IReadOnlyList<RateCardVersion> versions)
    {
        Check(versions);
        return new(id, carrierId, RateCardVersionRules.Ordered(versions)) { FinanceOwned = true };
    }

    /// <summary>Takes over what the seed file says now, silently, and only while Finance has not created or edited the card.</summary>
    public void Reload(CarrierId carrierId, IReadOnlyList<RateCardVersion> versions)
    {
        ArgumentNullException.ThrowIfNull(versions);
        if (FinanceOwned)
        {
            return;
        }

        CarrierId = carrierId;
        Versions = versions;
    }

    /// <summary>
    /// Finance's edit: replaces the whole list of dated versions and raises the Master-data revision. A repeat of what the card already
    /// is changes nothing, raises no revision and leaves the seed file in charge; it answers false so nothing is published.
    /// </summary>
    /// <returns>True when the edit changed the card.</returns>
    /// <exception cref="ArgumentException">The versions break <see cref="RateCardVersionRules"/>; nothing changed.</exception>
    public bool Edit(CarrierId carrierId, IReadOnlyList<RateCardVersion> versions)
    {
        Check(versions);
        if (CarrierId == carrierId && RateCardVersionRules.Same(Versions, versions))
        {
            return false;
        }

        CarrierId = carrierId;
        Versions = RateCardVersionRules.Ordered(versions);
        FinanceOwned = true;
        Revision++;
        return true;
    }

    private static void Check(IReadOnlyList<RateCardVersion> versions)
    {
        if (RateCardVersionRules.Problem(versions) is { } problem)
        {
            throw new ArgumentException(problem, nameof(versions));
        }
    }
}
