using Ardalis.Specification;

namespace Norvia.Erp.Domain.RateCards;

/// <summary>Every CarrierRateCard, for the read-only master data lists, which put them in order of their IDs.</summary>
public sealed class CarrierRateCardsSpec : Specification<CarrierRateCard>
{
    public CarrierRateCardsSpec() => Query.AsNoTracking();
}
