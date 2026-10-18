using Ardalis.Specification;

namespace Norvia.Erp.Domain.RateCards;

/// <summary>The RateCard with an ID, which the seed loader reloads.</summary>
public sealed class RateCardByIdSpec : Specification<RateCard>
{
    public RateCardByIdSpec(RateCardId id) => Query.Where(card => card.Id == id);
}
