using Ardalis.Specification;

namespace Norvia.Erp.Domain.ShipperInvoices;

public sealed class ShipperInvoiceByNumberSpec : Specification<ShipperInvoice>
{
    public ShipperInvoiceByNumberSpec(InvoiceNumber number) => Query.AsNoTracking().Where(invoice => invoice.Id == number);
}
