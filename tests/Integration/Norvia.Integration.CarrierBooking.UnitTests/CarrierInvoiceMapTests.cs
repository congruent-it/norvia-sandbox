using System.Text.Json.Nodes;

namespace Norvia.Integration.CarrierBooking.UnitTests;

public sealed class CarrierInvoiceMapTests
{
    private const string CarrierId = "CAR-0001";
    private const string Correlation = "1c7b0d3e-5a54-4f6a-9d5f-6a2f3c0d9a11";

    private static MappedCarrierInvoice Map(string json) => CarrierInvoiceMap.ToCarrierInvoiceSubmission(json, CarrierId, Correlation);

    private static string Invoice(string lines, string invoiceNo = "VDT-1", string invoiceDate = "19-10-2026") =>
        $$"""{ "invoiceNo": "{{invoiceNo}}", "invoiceDate": "{{invoiceDate}}", "lines": {{lines}} }""";

    [Fact]
    public void ToCarrierInvoiceSubmission_APayloadNamingAnotherCarrierAndCorrelationId_StillUsesTheTokensCarrierAndApiManagementsCorrelationId()
    {
        var mapped = Map("""{ "invoiceNo": "VDT-1", "invoiceDate": "19-10-2026", "carrierId": "CAR-0002", "correlationId": "mine", "lines": [ { "bookingRef": "R-1", "amount": 10.00 } ] }""");

        mapped.ErpRequest!["carrierId"]!.GetValue<string>().Should().Be(CarrierId);
        mapped.ErpRequest["correlationId"]!.GetValue<string>().Should().Be(Correlation);
        mapped.ErpRequest.ToJsonString().Should().NotContain("CAR-0002").And.NotContain("mine");
    }

    [Fact]
    public void ToCarrierInvoiceSubmission_ALocalDate_BecomesTheCanonicalDate()
    {
        var mapped = Map(Invoice("""[ { "bookingRef": "R-1", "amount": 10.00 } ]""", invoiceDate: "05-01-2027"));

        mapped.ErpRequest!["invoiceDate"]!.GetValue<string>().Should().Be("2027-01-05");
    }

    [Fact]
    public void ToCarrierInvoiceSubmission_Amounts_GoOverAsTheCarrierWroteThem_WithoutRounding()
    {
        var mapped = Map(Invoice("""[ { "bookingRef": "R-1", "amount": 95.10 }, { "bookingRef": "R-2", "amount": 7 } ]"""));

        mapped.ErpRequest!["lines"]![0]!["amount"]!.GetValue<decimal>().Should().Be(95.10m);
        mapped.ErpRequest["lines"]![1]!["amount"]!.GetValue<decimal>().Should().Be(7m);
    }

    [Fact]
    public void ToCarrierInvoiceSubmission_ALineWithoutADescription_LeavesItOut()
    {
        var mapped = Map(Invoice("""[ { "bookingRef": "R-1", "amount": 10.00 } ]"""));

        mapped.ErpRequest!["lines"]![0]!.AsObject().ContainsKey("description").Should().BeFalse();
    }

    [Fact]
    public void ToCarrierInvoiceSubmission_AnInvoiceWithLines_KeepsTheirOrder()
    {
        var mapped = Map(Invoice("""[ { "bookingRef": "B", "amount": 1 }, { "bookingRef": "A", "amount": 2 } ]"""));

        mapped.ErpRequest!["lines"]!.AsArray().Select(line => line!["carrierBookingReference"]!.GetValue<string>()).Should().Equal("B", "A");
    }

    [Theory]
    [InlineData("not json", "JSON object")]
    [InlineData("[]", "JSON object")]
    [InlineData("""{ "invoiceDate": "19-10-2026", "lines": [ { "bookingRef": "R", "amount": 1 } ] }""", "invoiceNo")]
    [InlineData("""{ "invoiceNo": "  ", "invoiceDate": "19-10-2026", "lines": [ { "bookingRef": "R", "amount": 1 } ] }""", "invoiceNo")]
    [InlineData("""{ "invoiceNo": "VDT-1", "lines": [ { "bookingRef": "R", "amount": 1 } ] }""", "invoiceDate")]
    [InlineData("""{ "invoiceNo": "VDT-1", "invoiceDate": "2026-10-19", "lines": [ { "bookingRef": "R", "amount": 1 } ] }""", "invoiceDate")]
    [InlineData("""{ "invoiceNo": "VDT-1", "invoiceDate": "31-02-2026", "lines": [ { "bookingRef": "R", "amount": 1 } ] }""", "invoiceDate")]
    [InlineData("""{ "invoiceNo": "VDT-1", "invoiceDate": "19-10-2026" }""", "at least one line")]
    [InlineData("""{ "invoiceNo": "VDT-1", "invoiceDate": "19-10-2026", "lines": [] }""", "at least one line")]
    [InlineData("""{ "invoiceNo": "VDT-1", "invoiceDate": "19-10-2026", "lines": [ { "amount": 1 } ] }""", "Line 1 needs the bookingRef")]
    [InlineData("""{ "invoiceNo": "VDT-1", "invoiceDate": "19-10-2026", "lines": [ "R" ] }""", "Line 1 needs the bookingRef")]
    [InlineData("""{ "invoiceNo": "VDT-1", "invoiceDate": "19-10-2026", "lines": [ { "bookingRef": "R" } ] }""", "Line 1 needs an amount")]
    [InlineData("""{ "invoiceNo": "VDT-1", "invoiceDate": "19-10-2026", "lines": [ { "bookingRef": "R", "amount": 0 } ] }""", "Line 1 needs an amount")]
    [InlineData("""{ "invoiceNo": "VDT-1", "invoiceDate": "19-10-2026", "lines": [ { "bookingRef": "R", "amount": -5 } ] }""", "Line 1 needs an amount")]
    [InlineData("""{ "invoiceNo": "VDT-1", "invoiceDate": "19-10-2026", "lines": [ { "bookingRef": "R", "amount": "10.00" } ] }""", "Line 1 needs an amount")]
    [InlineData("""{ "invoiceNo": "VDT-1", "invoiceDate": "19-10-2026", "lines": [ { "bookingRef": "R", "amount": 10.005 } ] }""", "to the cent")]
    [InlineData("""{ "invoiceNo": "VDT-1", "invoiceDate": "19-10-2026", "lines": [ { "bookingRef": "R", "amount": 1 }, { "bookingRef": "S", "amount": 0 } ] }""", "Line 2 needs an amount")]
    [InlineData("""{ "invoiceNo": "VDT-1", "invoiceDate": "19-10-2026", "lines": [ { "bookingRef": "R", "amount": 1, "description": 5 } ] }""", "description of line 1")]
    [InlineData("""{ "invoiceNo": "VDT-1", "invoiceDate": "19-10-2026", "lines": [ { "bookingRef": "R", "amount": 1 }, { "bookingRef": "R", "amount": 2 } ] }""", "Line 2 repeats the bookingRef R")]
    public void ToCarrierInvoiceSubmission_AnInvoiceBreakingTheRules_IsInvalid_SayingWhy(string json, string reason)
    {
        var mapped = Map(json);

        mapped.ErpRequest.Should().BeNull("an invalid invoice never reaches the ERP");
        mapped.Problem.Should().Contain(reason);
    }

    [Fact]
    public void ToCarrierInvoiceSubmission_AnInvalidInvoiceThatNamedItsNumber_KeepsTheNumberForTheAnswer()
    {
        var mapped = Map(Invoice("[]", invoiceNo: "VDT-9"));

        mapped.InvoiceNumber.Should().Be("VDT-9");
    }

    [Fact]
    public void ToCarrierInvoiceSubmission_TooLongValues_AreInvalid()
    {
        Map(Invoice("""[ { "bookingRef": "R", "amount": 1 } ]""", invoiceNo: new string('x', 51))).Problem.Should().Contain("invoiceNo");
        Map(Invoice($$"""[ { "bookingRef": "{{new string('r', 201)}}", "amount": 1 } ]""")).Problem.Should().Contain("bookingRef");
        Map(Invoice($$"""[ { "bookingRef": "R", "amount": 1, "description": "{{new string('d', 501)}}" } ]""")).Problem.Should().Contain("description");
    }

    [Theory]
    [InlineData("", Correlation)]
    [InlineData(CarrierId, " ")]
    public void ToCarrierInvoiceSubmission_WithoutTheCarrierOrTheCorrelationId_Throws(string carrierId, string correlationId)
    {
        var map = () => CarrierInvoiceMap.ToCarrierInvoiceSubmission(Invoice("""[ { "bookingRef": "R", "amount": 1 } ]"""), carrierId, correlationId);

        map.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void ToJsonString_ThenFromJson_RoundTripsAMappedAndAnInvalidInvoice()
    {
        var mapped = Map(Invoice("""[ { "bookingRef": "R", "amount": 1 } ]"""));
        var invalid = Map(Invoice("[]"));

        JsonNode.DeepEquals(MappedCarrierInvoice.FromJson(mapped.ToJsonString()).ErpRequest, mapped.ErpRequest).Should().BeTrue();
        MappedCarrierInvoice.FromJson(invalid.ToJsonString()).Should().Be(invalid);
    }

    [Fact]
    public void FromJson_NotAnObject_Throws()
    {
        var read = () => MappedCarrierInvoice.FromJson("[]");

        read.Should().Throw<ArgumentException>();
    }
}
