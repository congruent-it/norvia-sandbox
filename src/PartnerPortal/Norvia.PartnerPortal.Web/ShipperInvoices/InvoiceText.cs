using System.Globalization;
using Norvia.PartnerPortal.Domain.ShipperInvoices;

namespace Norvia.PartnerPortal.Web.ShipperInvoices;

/// <summary>The words and figures the invoices inbox uses.</summary>
public static class InvoiceText
{
    public static string Of(InvoiceDocumentType type) => type switch
    {
        InvoiceDocumentType.Invoice => "Invoice",
        InvoiceDocumentType.CreditNote => "Credit note",
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, "The inbox has no words for this type."),
    };

    public static string Of(ChargeCode code) => code switch
    {
        ChargeCode.Freight => "Freight",
        ChargeCode.MinimumChargeAdjustment => "Minimum charge adjustment",
        _ => throw new ArgumentOutOfRangeException(nameof(code), code, "The inbox has no words for this code."),
    };

    /// <summary>An amount in euros, such as <c>€ 1,234.50</c>, written the same on every host.</summary>
    public static string Money(decimal amount) => "€ " + amount.ToString("N2", CultureInfo.InvariantCulture);

    /// <summary>What a Credit note corrects, such as "Credits INV-2030-000001, TO-0000042: Rate Card error"; empty for an invoice.</summary>
    public static string Credits(CreditedLine? credit) =>
        credit is null ? string.Empty : $"Credits {credit.InvoiceNumber}, {credit.TransportOrderNumber}: {credit.Reason}";
}
