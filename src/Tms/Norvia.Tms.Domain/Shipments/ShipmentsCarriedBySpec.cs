using Ardalis.Specification;

namespace Norvia.Tms.Domain.Shipments;

/// <summary>The Shipments one Carrier or the Own fleet carries, for finding the Transport Orders a list is filtered to.</summary>
public sealed class ShipmentsCarriedBySpec : Specification<Shipment>
{
    public ShipmentsCarriedBySpec(CarriedBy carriedBy)
    {
        ArgumentNullException.ThrowIfNull(carriedBy);
        var carrierId = carriedBy.CarrierId;
        if (carrierId is null)
        {
            Query.Where(shipment => shipment.CarriedBy.CarrierId == null);
        }
        else
        {
            Query.Where(shipment => shipment.CarriedBy.CarrierId == carrierId);
        }

        Query.AsNoTracking();
    }
}
