using Ardalis.Specification;

namespace Norvia.Tms.Domain.RateCards;

/// <summary>The replicas that belong to a Carrier, the highest Master-data revision first and the ID breaking a tie.</summary>
public sealed class CarrierRateCardCopyOfCarrierSpec : Specification<CarrierRateCardCopy>
{
    public CarrierRateCardCopyOfCarrierSpec(string carrierId)
    {
        Query.AsNoTracking().Where(copy => copy.CarrierId == carrierId).OrderByDescending(copy => copy.Revision).ThenBy(copy => copy.Id);
    }
}
