using Ardalis.Specification;

namespace Norvia.Erp.Domain.CarrierInvoices;

/// <summary>Every Received Carrier invoice, the oldest first, for the list Finance decides from.</summary>
public sealed class CarrierInvoicesToDecideSpec : Specification<CarrierInvoice>
{
    public CarrierInvoicesToDecideSpec() =>
        Query.AsNoTracking().Where(invoice => invoice.Status == CarrierInvoiceStatus.Received).OrderBy(invoice => invoice.ReceivedAt).ThenBy(invoice => invoice.Id);
}
