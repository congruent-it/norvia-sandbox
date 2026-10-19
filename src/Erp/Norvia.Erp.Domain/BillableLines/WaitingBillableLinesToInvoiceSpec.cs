using Ardalis.Specification;
using Norvia.Erp.Domain.Shippers;

namespace Norvia.Erp.Domain.BillableLines;

/// <summary>
/// The Waiting Billable lines of one Shipper, tracked, for an Invoice run to set to Invoiced. A concurrent run that changed one of them first
/// makes the save conflict, so no line is invoiced twice. The monthly run passes a cutoff and takes only the lines delivered before it.
/// </summary>
public sealed class WaitingBillableLinesToInvoiceSpec : Specification<BillableLine>
{
    public WaitingBillableLinesToInvoiceSpec(ShipperId shipperId, DateTimeOffset? deliveredBefore = null)
    {
        Query.Where(line => line.Status == BillableLineStatus.Waiting && line.ShipperId == shipperId);
        if (deliveredBefore is { } cutoff)
        {
            Query.Where(line => line.DeliveryTime < cutoff);
        }

        Query.OrderBy(line => line.DeliveryTime).ThenBy(line => line.Id);
    }
}
