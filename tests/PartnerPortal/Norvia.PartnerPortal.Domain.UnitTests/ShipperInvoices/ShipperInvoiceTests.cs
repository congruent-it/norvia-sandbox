using Norvia.PartnerPortal.Domain.Personas;
using Norvia.PartnerPortal.Domain.ShipperInvoices;

namespace Norvia.PartnerPortal.Domain.UnitTests.ShipperInvoices;

public sealed class ShipperInvoiceTests
{
    private static readonly DateTimeOffset ReceivedAt = new(2026, 11, 1, 5, 0, 0, TimeSpan.Zero);
    private static readonly PersonaId Brouwer = new("brouwer-dranken");

    private static readonly ShipperInvoiceContent Content = new(
        InvoiceDocumentType.Invoice, "INV-2030-000001", new DateOnly(2030, 11, 1), null,
        [new ShipperInvoiceLine("TO-0000001", "BD-2030-0001", new DateOnly(2030, 10, 18), [new ShipperChargeLine(ChargeCode.Freight, 80.00m)], 80.00m)],
        80.00m, 16.80m, 96.80m);

    [Fact]
    public void Receive_KeepsWhatNorviaSent_AndKeysItOnTheDocumentNumber()
    {
        var invoice = ShipperInvoice.Receive(Brouwer, "corr-1", ReceivedAt, Content);

        invoice.PersonaId.Should().Be(Brouwer);
        invoice.DocumentNumber.Should().Be("INV-2030-000001");
        invoice.CorrelationId.Should().Be("corr-1");
        invoice.ReceivedAt.Should().Be(ReceivedAt);
        invoice.Content.Should().BeSameAs(Content);
    }

    [Fact]
    public void Receive_GivesEachDocumentAnIdOfItsOwn() =>
        ShipperInvoice.Receive(Brouwer, "corr-1", ReceivedAt, Content).Id.Should().NotBe(ShipperInvoice.Receive(Brouwer, "corr-1", ReceivedAt, Content).Id);

    [Fact]
    public void Receive_WithoutWhatItNeeds_Throws()
    {
        var withoutPersona = () => ShipperInvoice.Receive(null!, "corr-1", ReceivedAt, Content);
        var withoutCorrelation = () => ShipperInvoice.Receive(Brouwer, " ", ReceivedAt, Content);
        var withoutContent = () => ShipperInvoice.Receive(Brouwer, "corr-1", ReceivedAt, null!);
        var withoutNumber = () => ShipperInvoice.Receive(Brouwer, "corr-1", ReceivedAt, Content with { DocumentNumber = "" });

        withoutPersona.Should().Throw<ArgumentNullException>();
        withoutCorrelation.Should().Throw<ArgumentException>();
        withoutContent.Should().Throw<ArgumentNullException>();
        withoutNumber.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData(InvoiceDocumentType.Invoice, "INVOICE")]
    [InlineData(InvoiceDocumentType.CreditNote, "CREDIT_NOTE")]
    public void DocumentTypes_AreSpelledAsTheFormatSpellsThem(InvoiceDocumentType type, string wire)
    {
        InvoiceDocumentTypes.WireNameOf(type).Should().Be(wire);
        InvoiceDocumentTypes.TryParse(wire, out var parsed).Should().BeTrue();
        parsed.Should().Be(type);
    }

    [Theory]
    [InlineData(ChargeCode.Freight, "FREIGHT")]
    [InlineData(ChargeCode.MinimumChargeAdjustment, "MINIMUM_CHARGE_ADJUSTMENT")]
    public void ChargeCodes_AreSpelledAsTheFormatSpellsThem(ChargeCode code, string wire)
    {
        ChargeCodes.WireNameOf(code).Should().Be(wire);
        ChargeCodes.TryParse(wire, out var parsed).Should().BeTrue();
        parsed.Should().Be(code);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("invoice")]
    [InlineData("REFUND")]
    public void TryParse_AnythingElse_IsNoDocumentTypeOrChargeCode(string? wire)
    {
        InvoiceDocumentTypes.TryParse(wire, out _).Should().BeFalse();
        ChargeCodes.TryParse(wire, out _).Should().BeFalse();
    }

    [Fact]
    public void WireNames_ListEveryValue()
    {
        InvoiceDocumentTypes.WireNames.Should().BeEquivalentTo("INVOICE", "CREDIT_NOTE");
        ChargeCodes.WireNames.Should().BeEquivalentTo("FREIGHT", "MINIMUM_CHARGE_ADJUSTMENT");
    }

    [Fact]
    public void LocalDateText_ReadsAndWritesDayMonthYear()
    {
        LocalDateText.Of(new DateOnly(2030, 11, 1)).Should().Be("01-11-2030");
        LocalDateText.TryParse("01-11-2030", out var date).Should().BeTrue();
        date.Should().Be(new DateOnly(2030, 11, 1));
        LocalDateText.TryParse("2030-11-01", out _).Should().BeFalse();
        LocalDateText.TryParse("31-02-2030", out _).Should().BeFalse();
        LocalDateText.TryParse(null, out _).Should().BeFalse();
    }
}
