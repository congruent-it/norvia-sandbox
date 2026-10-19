using System.Text.Json.Nodes;
using Norvia.PartnerPortal.Domain.ShipperInvoices;

namespace Norvia.PartnerPortal.Infrastructure.UnitTests.PartnerApi;

public sealed class ShipperInvoiceSchemaTests
{
    public static TheoryData<string> Samples() => new() { "invoice", "credit-note" };

    private static JsonObject Sample(string name) => JsonNode.Parse(Contracts.Read($"PartnerApi/samples/shipper-invoices/{name}.shipper-invoice.json"))!.AsObject();

    [Theory]
    [MemberData(nameof(Samples))]
    public void ASample_FollowsTheSchema(string name) =>
        Contracts.ShipperInvoiceSchemaErrors(Sample(name)).Should().BeEmpty();

    [Fact]
    public void TheSchema_ListsTheDocumentTypesAndChargeCodesThePortalTakes()
    {
        var schema = JsonNode.Parse(Contracts.Read("PartnerApi/shipper-invoice.v1.schema.json"))!;

        schema["properties"]!["documentType"]!["enum"]!.AsArray().Select(node => node!.GetValue<string>()).Should().BeEquivalentTo(InvoiceDocumentTypes.WireNames);
        schema["properties"]!["lines"]!["items"]!["properties"]!["chargeLines"]!["items"]!["properties"]!["code"]!["enum"]!.AsArray()
            .Select(node => node!.GetValue<string>()).Should().BeEquivalentTo(ChargeCodes.WireNames);
    }

    [Theory]
    [InlineData("shipmentNumber")]
    [InlineData("carrier")]
    public void TheSchema_HasNoRoomForTheCarrierOrTheShipmentNumber(string field)
    {
        var invoice = Sample("invoice");
        invoice[field] = "SH-0000001";

        Contracts.ShipperInvoiceSchemaErrors(invoice).Should().NotBeEmpty();
    }

    [Fact]
    public void ALineCannotCarryTheShipmentNumber()
    {
        var invoice = Sample("invoice");
        invoice["lines"]![0]!["shipmentNumber"] = "SH-0000001";

        Contracts.ShipperInvoiceSchemaErrors(invoice).Should().NotBeEmpty();
    }

    [Theory]
    [InlineData("documentType", "REFUND")]
    [InlineData("documentNumber", "INV-30-1")]
    [InlineData("documentNumber", "CN-2030-000001")]
    [InlineData("documentDate", "2030-11-01")]
    [InlineData("currency", "USD")]
    [InlineData("vat", 27.305)]
    [InlineData("totalWithVat", -1)]
    public void AnInvoice_BreakingTheFormat_FailsTheSchema(string field, object value)
    {
        var invoice = Sample("invoice");
        invoice[field] = JsonValue.Create(value);

        Contracts.ShipperInvoiceSchemaErrors(invoice).Should().NotBeEmpty();
    }

    [Theory]
    [InlineData("totalWithoutVat")]
    [InlineData("vat")]
    [InlineData("totalWithVat")]
    [InlineData("lines")]
    [InlineData("currency")]
    [InlineData("documentDate")]
    public void AnInvoice_WithoutARequiredPart_FailsTheSchema(string field)
    {
        var invoice = Sample("invoice");
        invoice.Remove(field);

        Contracts.ShipperInvoiceSchemaErrors(invoice).Should().NotBeEmpty();
    }

    [Theory]
    [InlineData("creditedInvoiceNumber")]
    [InlineData("creditedTransportOrderNumber")]
    [InlineData("reason")]
    public void ACreditNote_WithoutWhatItCredits_FailsTheSchema(string field)
    {
        var creditNote = Sample("credit-note");
        creditNote.Remove(field);

        Contracts.ShipperInvoiceSchemaErrors(creditNote).Should().NotBeEmpty();
    }

    [Fact]
    public void AnInvoice_NamingWhatItCredits_FailsTheSchema()
    {
        var invoice = Sample("invoice");
        invoice["reason"] = "Because";

        Contracts.ShipperInvoiceSchemaErrors(invoice).Should().NotBeEmpty();
    }

    [Fact]
    public void ALineWithAnUnknownChargeCode_FailsTheSchema()
    {
        var invoice = Sample("invoice");
        invoice["lines"]![0]!["chargeLines"]![0]!["code"] = "FUEL";

        Contracts.ShipperInvoiceSchemaErrors(invoice).Should().NotBeEmpty();
    }
}
