using Ardalis.Specification;
using Norvia.Erp.Domain.Carriers;

namespace Norvia.Erp.Domain.CarrierInvoices;

/// <summary>One Carrier invoice, tracked, for the Carrier invoice outcome Logic App's report on it.</summary>
public sealed class CarrierInvoiceToReportSpec : Specification<CarrierInvoice>
{
    public CarrierInvoiceToReportSpec(CarrierId carrierId, string invoiceNumber) =>
        Query.Where(invoice => invoice.CarrierId == carrierId && invoice.InvoiceNumber == invoiceNumber);
}
