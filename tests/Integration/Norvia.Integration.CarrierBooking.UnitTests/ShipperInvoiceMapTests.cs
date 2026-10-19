using System.Text.Json.Nodes;

namespace Norvia.Integration.CarrierBooking.UnitTests;

public sealed class ShipperInvoiceMapTests
{
    private static JsonObject Issued() =>
        JsonNode.Parse(Contracts.Read("CanonicalModel/samples/shipper-invoice-issued/freight-and-minimum-charge.json"))!.AsObject();

    private static JsonObject? Map(JsonObject issued) => ShipperInvoiceMap.ToShipperInvoice(issued.ToJsonString());

    [Fact]
    public void ToShipperInvoice_TheInvoice_IsAnInvoiceWithItsNumberAndDateAsTheShipperFormatHasThem()
    {
        var invoice = Map(Issued())!;

        invoice["documentType"]!.GetValue<string>().Should().Be("INVOICE");
        invoice["documentNumber"]!.GetValue<string>().Should().Be("INV-2030-000001");
        invoice["documentDate"]!.GetValue<string>().Should().Be("01-11-2030");
        invoice["currency"]!.GetValue<string>().Should().Be("EUR");
    }

    [Fact]
    public void ToShipperInvoice_ALine_GivesTheShippersReferenceAsBookingRef_AndTheChargeLinesTheirCodes()
    {
        var invoice = Map(Issued())!;

        var line = invoice["lines"]![1]!;
        line["bookingRef"]!.GetValue<string>().Should().Be("BD-2030-0002");
        line["deliveryDate"]!.GetValue<string>().Should().Be("21-10-2030");
        line["chargeLines"]!.AsArray().Select(charge => charge!["code"]!.GetValue<string>()).Should().Equal("FREIGHT", "MINIMUM_CHARGE_ADJUSTMENT");
    }

    [Fact]
    public void ToShipperInvoice_LeavesOutWhatIsNorviasOwn()
    {
        var text = Map(Issued())!.ToJsonString();

        text.Should().NotContain("Brouwer").And.NotContain("c1d2e3f4").And.NotContain("Issued").And.NotContain("shipper\"");
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("{}")]
    public void ToShipperInvoice_TextThatIsNotAnIssuedInvoice_IsUnreadable(string text) =>
        ShipperInvoiceMap.ToShipperInvoice(text).Should().BeNull();

    [Fact]
    public void ToShipperInvoice_AnotherEventType_IsUnreadable()
    {
        var issued = Issued();
        issued["eventType"] = "CreditNoteIssued";

        Map(issued).Should().BeNull();
    }

    [Theory]
    [InlineData("invoiceNumber")]
    [InlineData("invoiceDate")]
    [InlineData("lines")]
    [InlineData("totalWithoutVat")]
    [InlineData("vat")]
    [InlineData("totalWithVat")]
    [InlineData("currency")]
    public void ToShipperInvoice_AnInvoiceWithoutAField_IsUnreadable(string field)
    {
        var issued = Issued();
        issued["invoice"]!.AsObject().Remove(field);

        Map(issued).Should().BeNull();
    }

    [Theory]
    [InlineData("transportOrderNumber")]
    [InlineData("shipperReference")]
    [InlineData("deliveryDate")]
    [InlineData("chargeLines")]
    [InlineData("total")]
    public void ToShipperInvoice_ALineWithoutAField_IsUnreadable(string field)
    {
        var issued = Issued();
        issued["invoice"]!["lines"]![0]!.AsObject().Remove(field);

        Map(issued).Should().BeNull();
    }

    [Fact]
    public void ToShipperInvoice_AChargeLineOfAnUnknownType_IsUnreadable()
    {
        var issued = Issued();
        issued["invoice"]!["lines"]![0]!["chargeLines"]![0]!["type"] = "Detention";

        Map(issued).Should().BeNull();
    }

    [Fact]
    public void ToShipperInvoice_AChargeLineWithoutAnAmount_IsUnreadable()
    {
        var issued = Issued();
        issued["invoice"]!["lines"]![0]!["chargeLines"]![0]!.AsObject().Remove("amount");

        Map(issued).Should().BeNull();
    }

    [Fact]
    public void ToShipperInvoice_ADateThatIsNotADate_IsUnreadable()
    {
        var issued = Issued();
        issued["invoice"]!["invoiceDate"] = "01-11-2030";

        Map(issued).Should().BeNull();
    }

    [Fact]
    public void ToShipperInvoice_AnInvoiceWithoutLines_IsUnreadable()
    {
        var issued = Issued();
        issued["invoice"]!["lines"] = new JsonArray();

        Map(issued).Should().BeNull();
    }

    [Fact]
    public void ToShipperInvoice_ALineThatIsNotAnObject_IsUnreadable()
    {
        var issued = Issued();
        issued["invoice"]!["lines"] = new JsonArray("a line");

        Map(issued).Should().BeNull();
    }

    [Fact]
    public void ToShipperInvoice_AChargeLineThatIsNotAnObject_IsUnreadable()
    {
        var issued = Issued();
        issued["invoice"]!["lines"]![0]!["chargeLines"] = new JsonArray("a charge line");

        Map(issued).Should().BeNull();
    }
}
