using Ardalis.Specification;

namespace Norvia.Tms.Domain.RateCards;

/// <summary>The replica with an ID, tracked so a replacement can change it.</summary>
public sealed class CarrierRateCardCopyForReplaceSpec : Specification<CarrierRateCardCopy>
{
    public CarrierRateCardCopyForReplaceSpec(string id)
    {
        Query.Where(copy => copy.Id == id);
    }
}
