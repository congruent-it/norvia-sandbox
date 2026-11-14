using Norvia.Tms.Domain.RateCards;
using Norvia.Tms.Domain.Shippers;

namespace Norvia.Tms.Application.RateCards;

/// <summary>The TMS's read copy of the Shippers' Rate Cards (ADR 0006).</summary>
public interface IRateCardDirectory
{
    /// <summary>The Shipper's Rate Card, or null when it has none.</summary>
    RateCard? FindFor(ShipperId shipperId);

    /// <summary>The Rate Card with this ID, or null when there is none. Its revision is the one a replacement must beat.</summary>
    RateCard? Find(RateCardId id);
}
