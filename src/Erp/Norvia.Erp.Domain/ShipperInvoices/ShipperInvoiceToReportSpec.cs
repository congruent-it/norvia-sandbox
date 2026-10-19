using Ardalis.Specification;

namespace Norvia.Erp.Domain.ShipperInvoices;

/// <summary>One Shipper invoice, tracked, for the Shipper invoice Logic App's report on it.</summary>
public sealed class ShipperInvoiceToReportSpec : Specification<ShipperInvoice>
{
    public ShipperInvoiceToReportSpec(InvoiceNumber number) => Query.Where(invoice => invoice.Id == number);
}
