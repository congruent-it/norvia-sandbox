namespace Norvia.PartnerPortal.Domain.CarrierInvoices;

public readonly record struct CarrierInvoiceId(Guid Value)
{
    public static CarrierInvoiceId New() => new(Guid.CreateVersion7());
}
