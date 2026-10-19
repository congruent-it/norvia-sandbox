using Norvia.Erp.Application.ShipperInvoices;

namespace Norvia.Erp.Web.UnitTests.ShipperInvoices;

internal static class ShipperInvoicesTestData
{
    public static ShipperInvoiceView Invoice(int sequence, decimal withoutVat = 100m, int lines = 1, DateTimeOffset? sentAt = null) => new(
        $"INV-2026-{sequence:D6}",
        new DateOnly(2026, 10, 19),
        new ShipperOnInvoiceView("SHP-0001", "Brouwer Dranken"),
        [.. Enumerable.Range(1, lines).Select(index => Line(index, withoutVat / lines))],
        "EUR",
        withoutVat,
        decimal.Round(withoutVat * 0.21m, 2, MidpointRounding.AwayFromZero),
        withoutVat + decimal.Round(withoutVat * 0.21m, 2, MidpointRounding.AwayFromZero),
        new DateTimeOffset(2026, 10, 19, 8, 0, 0, TimeSpan.Zero),
        $"corr-{sequence}",
        sentAt is null ? "Issued" : "Sent",
        sentAt);

    private static ShipperInvoiceLineView Line(int index, decimal total) => new(
        $"TO-{index:D7}",
        $"PO-{index}",
        $"SH-{index:D7}",
        new DateOnly(2026, 10, 17),
        [new InvoiceChargeLineView("Freight", total - 5m), new InvoiceChargeLineView("MinimumChargeAdjustment", 5m)],
        total);
}
