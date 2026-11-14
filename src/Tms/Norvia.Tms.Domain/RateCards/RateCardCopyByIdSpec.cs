using Ardalis.Specification;

namespace Norvia.Tms.Domain.RateCards;

/// <summary>The replica with an ID, to read.</summary>
public sealed class RateCardCopyByIdSpec : Specification<RateCardCopy>
{
    public RateCardCopyByIdSpec(string id)
    {
        Query.AsNoTracking().Where(copy => copy.Id == id);
    }
}
