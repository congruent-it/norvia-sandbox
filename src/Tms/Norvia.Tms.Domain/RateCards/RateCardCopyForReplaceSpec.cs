using Ardalis.Specification;

namespace Norvia.Tms.Domain.RateCards;

/// <summary>The replica with an ID, tracked so a replacement can change it.</summary>
public sealed class RateCardCopyForReplaceSpec : Specification<RateCardCopy>
{
    public RateCardCopyForReplaceSpec(string id)
    {
        Query.Where(copy => copy.Id == id);
    }
}
