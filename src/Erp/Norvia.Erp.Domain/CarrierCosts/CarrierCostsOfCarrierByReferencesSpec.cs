using Ardalis.Specification;
using Norvia.Erp.Domain.Carriers;

namespace Norvia.Erp.Domain.CarrierCosts;

/// <summary>
/// The Carrier costs of one Carrier with any of the given Carrier booking references, for matching the lines of its Carrier invoice.
/// A reference of another Carrier is left out, so it matches nothing.
/// </summary>
public sealed class CarrierCostsOfCarrierByReferencesSpec : Specification<CarrierCost>
{
    public CarrierCostsOfCarrierByReferencesSpec(CarrierId carrierId, IReadOnlyCollection<string> carrierBookingReferences) =>
        Query.Where(cost => cost.CarrierId == carrierId && carrierBookingReferences.Contains(cost.CarrierBookingReference));
}
