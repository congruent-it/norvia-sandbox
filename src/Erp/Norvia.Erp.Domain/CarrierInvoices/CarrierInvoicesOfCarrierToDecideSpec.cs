using Ardalis.Specification;
using Norvia.Erp.Domain.Carriers;

namespace Norvia.Erp.Domain.CarrierInvoices;

/// <summary>
/// The Received Carrier invoices of one Carrier, the oldest first and tracked, so a Carrier cost that arrives late can match their
/// Unknown booking lines again. The lines are one document, so the caller picks the lines.
/// </summary>
public sealed class CarrierInvoicesOfCarrierToDecideSpec : Specification<CarrierInvoice>
{
    public CarrierInvoicesOfCarrierToDecideSpec(CarrierId carrierId) =>
        Query.Where(invoice => invoice.CarrierId == carrierId && invoice.Status == CarrierInvoiceStatus.Received)
            .OrderBy(invoice => invoice.ReceivedAt).ThenBy(invoice => invoice.Id);
}
