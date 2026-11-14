using Ardalis.Specification;

namespace Norvia.Tms.Domain.RateCards;

/// <summary>The replicas that belong to a Shipper, the highest Master-data revision first and the ID breaking a tie.</summary>
public sealed class RateCardCopyOfShipperSpec : Specification<RateCardCopy>
{
    public RateCardCopyOfShipperSpec(string shipperId)
    {
        Query.AsNoTracking().Where(copy => copy.ShipperId == shipperId).OrderByDescending(copy => copy.Revision).ThenBy(copy => copy.Id);
    }
}
