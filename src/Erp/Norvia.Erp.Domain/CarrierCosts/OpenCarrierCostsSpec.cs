using Ardalis.Specification;

namespace Norvia.Erp.Domain.CarrierCosts;

/// <summary>Every Open Carrier cost, the earliest delivery first, for the list Finance reads.</summary>
public sealed class OpenCarrierCostsSpec : Specification<CarrierCost>
{
    public OpenCarrierCostsSpec() =>
        Query.AsNoTracking().Where(cost => cost.Status == CarrierCostStatus.Open).OrderBy(cost => cost.DeliveryTime).ThenBy(cost => cost.Id);
}
