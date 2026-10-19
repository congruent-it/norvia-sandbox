namespace Norvia.PartnerPortal.Domain.CarrierInvoices;

public readonly record struct CarrierInvoiceOutcomeId(Guid Value)
{
    public static CarrierInvoiceOutcomeId New() => new(Guid.CreateVersion7());
}
