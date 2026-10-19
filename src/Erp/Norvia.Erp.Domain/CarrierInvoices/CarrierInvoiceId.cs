namespace Norvia.Erp.Domain.CarrierInvoices;

/// <summary>
/// A Carrier invoice is identified by an ID the ERP creates. The Carrier's own invoice number is unique per Carrier, not across Carriers,
/// so it can't be the key.
/// </summary>
public readonly record struct CarrierInvoiceId(Guid Value)
{
    public static CarrierInvoiceId New() => new(Guid.CreateVersion7());

    public override string ToString() => Value.ToString();
}
