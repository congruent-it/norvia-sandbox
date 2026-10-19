using Ardalis.Specification;

namespace Norvia.Erp.Domain.ShipperInvoices;

/// <summary>Every Shipper invoice, the newest first, for the list Finance reads.</summary>
public sealed class ShipperInvoicesNewestFirstSpec : Specification<ShipperInvoice>
{
    public ShipperInvoicesNewestFirstSpec() =>
        Query.AsNoTracking().OrderByDescending(invoice => invoice.IssuedAt).ThenByDescending(invoice => invoice.Id);
}
