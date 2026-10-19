namespace Norvia.PartnerPortal.Domain.ShipperInvoices;

public readonly record struct ShipperInvoiceId(Guid Value)
{
    public static ShipperInvoiceId New() => new(Guid.CreateVersion7());
}
