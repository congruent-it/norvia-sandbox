using Ardalis.Specification;
using Norvia.Erp.Domain.Carriers;

namespace Norvia.Erp.Domain.RateCards;

/// <summary>The Carrier Rate Cards of one Carrier, of which it may have one.</summary>
public sealed class CarrierRateCardOfCarrierSpec : Specification<CarrierRateCard>
{
    public CarrierRateCardOfCarrierSpec(CarrierId carrierId) => Query.Where(card => card.CarrierId == carrierId).AsNoTracking();
}
