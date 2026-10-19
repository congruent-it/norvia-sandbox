using System.Text.Json.Nodes;

namespace Norvia.Integration.CarrierBooking.UnitTests;

public sealed class CreditNoteMapTests
{
    private static JsonObject Issued() =>
        JsonNode.Parse(Contracts.Read("CanonicalModel/samples/credit-note-issued/partial-credit.json"))!.AsObject();

    private static JsonObject? Map(JsonObject issued) => ShipperInvoiceMap.ToCreditNote(issued.ToJsonString());

    [Fact]
    public void ToCreditNote_IsACreditNoteWithItsNumberAndDateAsTheShipperFormatHasThem()
    {
        var note = Map(Issued())!;

        note["documentType"]!.GetValue<string>().Should().Be("CREDIT_NOTE");
        note["documentNumber"]!.GetValue<string>().Should().Be("CN-2030-000001");
        note["documentDate"]!.GetValue<string>().Should().Be("05-11-2030");
        note["currency"]!.GetValue<string>().Should().Be("EUR");
    }

    [Fact]
    public void ToCreditNote_NamesTheInvoiceAndLineItCredits_AndTheReason()
    {
        var note = Map(Issued())!;

        note["creditedInvoiceNumber"]!.GetValue<string>().Should().Be("INV-2030-000001");
        note["creditedTransportOrderNumber"]!.GetValue<string>().Should().Be("TO-0000002");
        note["reason"]!.GetValue<string>().Should().Be("Rate Card error on Lane NL-BE, see ticket 4411");
    }

    [Fact]
    public void ToCreditNote_HasOneLine_WithTheShippersReferenceAsBookingRef_AndTheCreditedAmountAsOneFreightCharge()
    {
        var note = Map(Issued())!;

        var line = note["lines"]!.AsArray().Should().ContainSingle().Subject!;
        line["transportOrderNumber"]!.GetValue<string>().Should().Be("TO-0000002");
        line["bookingRef"]!.GetValue<string>().Should().Be("BD-2030-0002");
        line["deliveryDate"]!.GetValue<string>().Should().Be("21-10-2030");
        line["lineTotal"]!.GetValue<decimal>().Should().Be(14.50m);
        var charge = line["chargeLines"]!.AsArray().Should().ContainSingle().Subject!;
        charge["code"]!.GetValue<string>().Should().Be("FREIGHT");
        charge["amount"]!.GetValue<decimal>().Should().Be(14.50m);
    }

    [Fact]
    public void ToCreditNote_KeepsTheAmountsTheErpGave()
    {
        var note = Map(Issued())!;

        (note["totalWithoutVat"]!.GetValue<decimal>(), note["vat"]!.GetValue<decimal>(), note["totalWithVat"]!.GetValue<decimal>())
            .Should().Be((14.50m, 3.05m, 17.55m));
    }

    [Fact]
    public void ToCreditNote_LeavesOutWhatIsNorviasOwn_AndTheShipmentNumber()
    {
        var text = Map(Issued())!.ToJsonString();

        text.Should().NotContain("Brouwer").And.NotContain("e1f2a3b4").And.NotContain("Issued").And.NotContain("SH-0").And.NotContain("shipmentNumber").And.NotContain("status");
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("{}")]
    public void ToCreditNote_TextThatIsNotAnIssuedCreditNote_IsUnreadable(string text) =>
        ShipperInvoiceMap.ToCreditNote(text).Should().BeNull();

    [Fact]
    public void ToCreditNote_AnotherEventType_IsUnreadable()
    {
        var issued = Issued();
        issued["eventType"] = "ShipperInvoiceIssued";

        Map(issued).Should().BeNull();
    }

    [Theory]
    [InlineData("creditNoteNumber")]
    [InlineData("creditNoteDate")]
    [InlineData("invoiceNumber")]
    [InlineData("transportOrderNumber")]
    [InlineData("shipperReference")]
    [InlineData("deliveryDate")]
    [InlineData("reason")]
    [InlineData("totalWithoutVat")]
    [InlineData("vat")]
    [InlineData("totalWithVat")]
    [InlineData("currency")]
    public void ToCreditNote_ACreditNoteWithoutAField_IsUnreadable(string field)
    {
        var issued = Issued();
        issued["creditNote"]!.AsObject().Remove(field);

        Map(issued).Should().BeNull();
    }

    [Fact]
    public void ToCreditNote_ADateThatIsNotADate_IsUnreadable()
    {
        var issued = Issued();
        issued["creditNote"]!["creditNoteDate"] = "05-11-2030";

        Map(issued).Should().BeNull();
    }

    [Fact]
    public void ToShipperDocument_ACreditNote_IsMappedAsACreditNote() =>
        JsonNode.DeepEquals(ShipperInvoiceMap.ToShipperDocument(Issued().ToJsonString()), Map(Issued())).Should().BeTrue();

    [Fact]
    public void ToShipperDocument_AnInvoice_IsMappedAsAnInvoice()
    {
        var invoice = Contracts.Read("CanonicalModel/samples/shipper-invoice-issued/freight-and-minimum-charge.json");

        ShipperInvoiceMap.ToShipperDocument(invoice)!["documentType"]!.GetValue<string>().Should().Be("INVOICE");
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("{}")]
    public void ToShipperDocument_TextThatIsNeither_IsUnreadable(string text) =>
        ShipperInvoiceMap.ToShipperDocument(text).Should().BeNull();
}
