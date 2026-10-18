using Ardalis.Specification;
using Norvia.Erp.Domain.Shippers;

namespace Norvia.Erp.Domain.BillableLines;

/// <summary>The Waiting Billable lines of one Shipper, the earliest delivery first.</summary>
public sealed class WaitingBillableLinesOfShipperSpec : Specification<BillableLine>
{
    public WaitingBillableLinesOfShipperSpec(ShipperId shipperId) =>
        Query.AsNoTracking()
            .Where(line => line.Status == BillableLineStatus.Waiting && line.ShipperId == shipperId)
            .OrderBy(line => line.DeliveryTime).ThenBy(line => line.Id);
}
