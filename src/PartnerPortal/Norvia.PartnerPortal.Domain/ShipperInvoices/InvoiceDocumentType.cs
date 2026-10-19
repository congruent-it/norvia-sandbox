namespace Norvia.PartnerPortal.Domain.ShipperInvoices;

/// <summary>What a document in the invoices inbox is, as the Shipper invoice format names it (E1): <c>INVOICE</c> or <c>CREDIT_NOTE</c>.</summary>
public enum InvoiceDocumentType
{
    Invoice,
    CreditNote,
}

public static class InvoiceDocumentTypes
{
    public static IReadOnlyList<string> WireNames { get; } = [.. Enum.GetValues<InvoiceDocumentType>().Select(WireNameOf)];

    public static string WireNameOf(InvoiceDocumentType type) => InvoiceWireName.Of(type);

    /// <summary>The type the format spells so, or false for any other text.</summary>
    public static bool TryParse(string? wire, out InvoiceDocumentType type) => InvoiceWireName.TryParse(wire, out type);
}
