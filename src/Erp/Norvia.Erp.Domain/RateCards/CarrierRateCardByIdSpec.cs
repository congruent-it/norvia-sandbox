using Ardalis.Specification;

namespace Norvia.Erp.Domain.RateCards;

/// <summary>The CarrierRateCard with an ID, which the seed loader reloads.</summary>
public sealed class CarrierRateCardByIdSpec : Specification<CarrierRateCard>
{
    public CarrierRateCardByIdSpec(RateCardId id) => Query.Where(card => card.Id == id);
}
