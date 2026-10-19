using Ardalis.Specification;

namespace Norvia.Erp.Domain.CarrierCosts;

/// <summary>The Carrier cost of a Carrier booking, which makes recording idempotent on the Carrier booking number.</summary>
public sealed class CarrierCostByBookingNumberSpec : Specification<CarrierCost>
{
    public CarrierCostByBookingNumberSpec(string carrierBookingNumber) =>
        Query.Where(cost => cost.Id == new CarrierCostId(carrierBookingNumber));
}
