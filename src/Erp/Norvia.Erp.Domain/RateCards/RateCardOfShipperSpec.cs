using Ardalis.Specification;
using Norvia.Erp.Domain.Shippers;

namespace Norvia.Erp.Domain.RateCards;

/// <summary>The Rate Cards of one Shipper, of which it may have one.</summary>
public sealed class RateCardOfShipperSpec : Specification<RateCard>
{
    public RateCardOfShipperSpec(ShipperId shipperId) => Query.Where(card => card.ShipperId == shipperId).AsNoTracking();
}
