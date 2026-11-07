using Ardalis.Specification;
using Norvia.Erp.Domain.Carriers;

namespace Norvia.Erp.Domain.CarrierCosts;

/// <summary>The Open Carrier costs of one Carrier, for the candidates of an Unknown booking line.</summary>
public sealed class OpenCarrierCostsOfCarrierSpec : Specification<CarrierCost>
{
    public OpenCarrierCostsOfCarrierSpec(CarrierId carrierId) =>
        Query.AsNoTracking().Where(cost => cost.CarrierId == carrierId && cost.Status == CarrierCostStatus.Open);
}
