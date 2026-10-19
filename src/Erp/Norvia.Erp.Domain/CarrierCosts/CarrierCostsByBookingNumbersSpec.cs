using Ardalis.Specification;

namespace Norvia.Erp.Domain.CarrierCosts;

/// <summary>The Carrier costs with any of the given Carrier booking numbers, for showing each Carrier invoice line next to its Carrier cost.</summary>
public sealed class CarrierCostsByBookingNumbersSpec : Specification<CarrierCost>
{
    public CarrierCostsByBookingNumbersSpec(IReadOnlyCollection<string> carrierBookingNumbers)
    {
        var ids = carrierBookingNumbers.Select(number => new CarrierCostId(number)).ToList();
        Query.AsNoTracking().Where(cost => ids.Contains(cost.Id));
    }
}
