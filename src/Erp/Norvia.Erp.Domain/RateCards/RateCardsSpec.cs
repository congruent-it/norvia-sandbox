using Ardalis.Specification;

namespace Norvia.Erp.Domain.RateCards;

/// <summary>Every RateCard, for the read-only master data lists, which put them in order of their IDs.</summary>
public sealed class RateCardsSpec : Specification<RateCard>
{
    public RateCardsSpec() => Query.AsNoTracking();
}
