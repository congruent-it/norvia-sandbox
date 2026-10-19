using Microsoft.EntityFrameworkCore;
using Norvia.PartnerPortal.Domain.ShipperInvoices;
using Norvia.PartnerPortal.Infrastructure.Data;

namespace Norvia.PartnerPortal.Infrastructure.UnitTests.Data;

/// <summary>How a Shipper invoice is stored, checked on the model alone: no database is involved.</summary>
public sealed class ShipperInvoiceConfigurationTests
{
    private static readonly string[] UniqueKey = ["PersonaId", "DocumentNumber"];

    private static PartnerPortalDbContext NewContext() =>
        new(new DbContextOptionsBuilder<PartnerPortalDbContext>().UseSqlServer("Server=unused").Options);

    [Fact]
    public void DocumentNumber_IsUniquePerPersona_SoARepeatedDocumentIsStoredOnce()
    {
        using var context = NewContext();

        context.Model.FindEntityType(typeof(ShipperInvoice))!.GetIndexes().Should().Contain(index =>
            index.IsUnique && index.Properties.Select(property => property.Name).SequenceEqual(UniqueKey));
    }

    [Fact]
    public void Content_IsKeptWhole_AndReadsBackWithItsLinesAndCredit()
    {
        using var context = NewContext();
        var property = context.Model.FindEntityType(typeof(ShipperInvoice))!.FindProperty(nameof(ShipperInvoice.Content))!;
        var converter = property.GetValueConverter()!;
        var content = new ShipperInvoiceContent(
            InvoiceDocumentType.CreditNote, "CN-2030-000001", new DateOnly(2030, 11, 5), new CreditedLine("INV-2030-000001", "TO-0000002", "Rate Card error"),
            [new ShipperInvoiceLine("TO-0000002", "BD-2030-0002", new DateOnly(2030, 10, 21), [new ShipperChargeLine(ChargeCode.MinimumChargeAdjustment, 14.50m)], 14.50m)],
            14.50m, 3.05m, 17.55m);

        var stored = (string)converter.ConvertToProvider(content)!;
        var read = (ShipperInvoiceContent)converter.ConvertFromProvider(stored)!;

        read.Should().BeEquivalentTo(content);
        stored.Should().Contain("CreditNote");
    }

    [Fact]
    public void Content_Comparer_SeesAChangeAndCopiesWhole()
    {
        using var context = NewContext();
        var comparer = context.Model.FindEntityType(typeof(ShipperInvoice))!.FindProperty(nameof(ShipperInvoice.Content))!.GetValueComparer()!;
        var content = new ShipperInvoiceContent(
            InvoiceDocumentType.Invoice, "INV-2030-000001", new DateOnly(2030, 11, 1), null,
            [new ShipperInvoiceLine("TO-0000001", "BD-2030-0001", new DateOnly(2030, 10, 18), [new ShipperChargeLine(ChargeCode.Freight, 80.00m)], 80.00m)],
            80.00m, 16.80m, 96.80m);

        var copy = comparer.Snapshot(content)!;

        comparer.Equals(content, copy).Should().BeTrue();
        comparer.GetHashCode(content).Should().Be(comparer.GetHashCode(copy));
        comparer.Equals(content, content with { Vat = 1m }).Should().BeFalse();
        copy.Should().NotBeSameAs(content);
    }
}
