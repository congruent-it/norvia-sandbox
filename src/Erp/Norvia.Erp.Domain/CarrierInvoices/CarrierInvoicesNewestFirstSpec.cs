using Ardalis.Specification;

namespace Norvia.Erp.Domain.CarrierInvoices;

/// <summary>Every Carrier invoice, the newest first, for the list Finance reads.</summary>
public sealed class CarrierInvoicesNewestFirstSpec : Specification<CarrierInvoice>
{
    public CarrierInvoicesNewestFirstSpec() =>
        Query.AsNoTracking().OrderByDescending(invoice => invoice.ReceivedAt).ThenByDescending(invoice => invoice.Id);
}
