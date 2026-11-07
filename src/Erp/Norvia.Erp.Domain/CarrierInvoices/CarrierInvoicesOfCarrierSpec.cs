using Ardalis.Specification;
using Norvia.Erp.Domain.Carriers;

namespace Norvia.Erp.Domain.CarrierInvoices;

/// <summary>
/// Every Carrier invoice of one Carrier, the oldest first, to find the earlier line that made a Carrier cost Invoiced. The lines are one
/// document, so the caller picks the line.
/// </summary>
public sealed class CarrierInvoicesOfCarrierSpec : Specification<CarrierInvoice>
{
    public CarrierInvoicesOfCarrierSpec(CarrierId carrierId) =>
        Query.AsNoTracking().Where(invoice => invoice.CarrierId == carrierId).OrderBy(invoice => invoice.ReceivedAt).ThenBy(invoice => invoice.Id);
}
