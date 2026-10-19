using Ardalis.Specification;
using Norvia.Erp.Domain.Carriers;

namespace Norvia.Erp.Domain.CarrierInvoices;

/// <summary>The Carrier invoice a Carrier sent under an invoice number, which makes receiving idempotent on the Carrier and the number.</summary>
public sealed class CarrierInvoiceByNumberSpec : Specification<CarrierInvoice>
{
    public CarrierInvoiceByNumberSpec(CarrierId carrierId, string invoiceNumber) =>
        Query.Where(invoice => invoice.CarrierId == carrierId && invoice.InvoiceNumber == invoiceNumber);
}
