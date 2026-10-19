using System.Text;
using System.Text.Json.Nodes;
using Norvia.Integration.TransportOrderIntake;

namespace Norvia.Integration.CarrierBooking.UnitTests;

/// <summary>The relay of the ERP's response to the Carrier, in the Carrier invoice response format.</summary>
public sealed class CarrierInvoiceResponsesTests
{
    private const string Correlation = "1c7b0d3e-5a54-4f6a-9d5f-6a2f3c0d9a11";

    private static readonly MappedCarrierInvoice Mapped = CarrierInvoiceMap.ToCarrierInvoiceSubmission(
        """{ "invoiceNo": "VDT-2026-0413", "invoiceDate": "20-10-2026", "lines": [ { "bookingRef": "TRX-1001", "amount": 95.00 } ] }""", "CAR-0001", Correlation);

    private static string Receipt(string status = "Received", string result = "PriceMismatch", string? approvedAmount = null, bool repeat = false) => $$"""
        {
          "invoice": {
            "carrierId": "CAR-0001", "invoiceNumber": "VDT-2026-0413", "invoiceDate": "2026-10-20", "currency": "EUR", "total": 95.00,
            "lines": [ { "carrierBookingReference": "TRX-1001", "amount": 95.00, "description": null, "result": "{{result}}", "status": "ToDecide", "carrierBookingNumber": "CB-0000042", "acceptedAmount": null, "remark": null } ],
            "correlationId": "{{Correlation}}", "receivedAt": "2026-10-20T08:00:00Z", "status": "{{status}}", "approvedAmount": {{approvedAmount ?? "null"}}, "approvedAt": null
          },
          "repeat": {{(repeat ? "true" : "false")}}
        }
        """;

    private static BookingResponse Relay(int status, string body) => CarrierInvoiceResponses.Relay(Mapped, status, body, Correlation);

    private static void ShouldKeepToTheSchema(BookingResponse response)
    {
        var results = Schemas.CarrierInvoiceResponse.Validate(response.Body);
        results.IsValid.Should().BeTrue($"{response.Body.ToJsonString()}: {Schemas.Errors(results)}");
    }

    [Fact]
    public void Relay_AReceivedInvoice_GivesEachLinesResultStraightAway()
    {
        var response = Relay(201, Receipt());

        response.StatusCode.Should().Be(201);
        response.FailureCode.Should().BeNull();
        JsonNode.DeepEquals(response.Body, JsonNode.Parse(Contracts.Read("PartnerApi/samples/carrier-invoice-responses/received-with-price-mismatch.json")))
            .Should().BeTrue(response.Body.ToJsonString());
        ShouldKeepToTheSchema(response);
    }

    [Fact]
    public void Relay_AnInvoiceWhoseLinesAllMatch_IsApprovedWithItsApprovedAmount()
    {
        var response = Relay(201, Receipt(status: "Approved", result: "Matched", approvedAmount: "95.00"));

        response.Body["status"]!.GetValue<string>().Should().Be("APPROVED");
        response.Body["approvedAmount"]!.GetValue<decimal>().Should().Be(95.00m);
        response.Body["lines"]![0]!["result"]!.GetValue<string>().Should().Be("MATCHED");
        ShouldKeepToTheSchema(response);
    }

    [Theory]
    [InlineData("Matched", "MATCHED")]
    [InlineData("PriceMismatch", "PRICE_MISMATCH")]
    [InlineData("UnknownBooking", "UNKNOWN_BOOKING")]
    [InlineData("Duplicate", "DUPLICATE")]
    public void Relay_EveryLineResult_IsTranslatedToTheCarriersVocabulary(string erpResult, string carrierResult)
    {
        var response = Relay(201, Receipt(result: erpResult));

        response.Body["lines"]![0]!["result"]!.GetValue<string>().Should().Be(carrierResult);
    }

    [Fact]
    public void Relay_ARepeat_IsRelayedAs200AndRepeated()
    {
        var response = Relay(200, Receipt(repeat: true));

        response.StatusCode.Should().Be(200);
        response.Body["repeated"]!.GetValue<bool>().Should().BeTrue();
        JsonNode.DeepEquals(response.Body, JsonNode.Parse(Contracts.Read("PartnerApi/samples/carrier-invoice-responses/received-repeated.json")))
            .Should().BeTrue(response.Body.ToJsonString());
    }

    [Theory]
    [InlineData(409, "INVOICE_NUMBER_USED")]
    [InlineData(422, "CARRIER_NOT_FOUND")]
    public void Relay_ARefusalOfTheErp_IsRelayedWithItsOwnStatusAndAReadableMessage_AndTheRunNeedNotFail(int status, string result)
    {
        var response = Relay(status, """{ "title": "x", "code": "Whatever" }""");

        response.StatusCode.Should().Be(status);
        response.FailureCode.Should().BeNull();
        response.Body["result"]!.GetValue<string>().Should().Be(result);
        response.Body["invoiceNo"]!.GetValue<string>().Should().Be("VDT-2026-0413");
        response.Body["messageId"]!.GetValue<string>().Should().Be(Correlation);
        response.Body["text"]!.GetValue<string>().Should().NotBeNullOrWhiteSpace();
        ShouldKeepToTheSchema(response);
    }

    [Fact]
    public void Relay_TheErpsProblemAsBase64Content_IsReadAllTheSame()
    {
        var content = Convert.ToBase64String(Encoding.UTF8.GetBytes("""{ "code": "CarrierInvoiceLineAmountNotAboveZero", "detail": "Line 1 has no amount." }"""));

        var response = Relay(400, $$"""{ "$content-type": "application/problem+json", "$content": "{{content}}" }""");

        response.StatusCode.Should().Be(400);
        response.Body["result"]!.GetValue<string>().Should().Be("INVALID");
        response.Body["text"]!.GetValue<string>().Should().Be("Line 1 has no amount.");
        response.FailureCode.Should().Be("CarrierInvoiceLineAmountNotAboveZero");
        ShouldKeepToTheSchema(response);
    }

    [Fact]
    public void Relay_A400WithoutAProblem_IsInvalidWithAGenericMessage()
    {
        var response = Relay(400, string.Empty);

        response.Body["text"]!.GetValue<string>().Should().NotBeNullOrWhiteSpace();
        response.FailureCode.Should().Be("ErpCallRefused");
    }

    [Fact]
    public void Relay_AnInvoiceTheMapCouldNotUse_IsInvalid_WithoutAnyErpAnswer()
    {
        var invalid = CarrierInvoiceMap.ToCarrierInvoiceSubmission("""{ "invoiceNo": "VDT-1", "invoiceDate": "20-10-2026", "lines": [] }""", "CAR-0001", Correlation);

        var response = CarrierInvoiceResponses.Relay(invalid, 0, string.Empty, Correlation);

        response.StatusCode.Should().Be(400);
        response.FailureCode.Should().Be("CarrierInvoiceUnreadable");
        response.Body["text"]!.GetValue<string>().Should().Contain("at least one line");
        ShouldKeepToTheSchema(response);
    }

    [Theory]
    [InlineData(500, "ErpUnavailable")]
    [InlineData(503, "ErpUnavailable")]
    [InlineData(408, "ErpUnavailable")]
    [InlineData(429, "ErpUnavailable")]
    [InlineData(0, "ErpUnavailable")]
    [InlineData(401, "ErpCallRefused")]
    [InlineData(403, "ErpCallRefused")]
    public void Relay_AnErpThatCouldNotTakeTheInvoiceIn_IsNotReceived_AndTheRunFailsWithTheCause(int status, string failureCode)
    {
        var response = Relay(status, string.Empty);

        response.StatusCode.Should().Be(502);
        response.FailureCode.Should().Be(failureCode);
        response.Body["result"]!.GetValue<string>().Should().Be("NOT_RECEIVED");
        ShouldKeepToTheSchema(response);
    }

    [Theory]
    [InlineData("""{ "repeat": false }""")]
    [InlineData("not json")]
    [InlineData("")]
    [InlineData("""{ "invoice": { "invoiceNumber": "OTHER", "status": "Received", "approvedAmount": null, "lines": [ { "carrierBookingReference": "R", "amount": 1, "result": "Matched" } ] }, "repeat": false }""")]
    [InlineData("""{ "invoice": { "invoiceNumber": "VDT-2026-0413", "status": "Paid", "approvedAmount": null, "lines": [ { "carrierBookingReference": "R", "amount": 1, "result": "Matched" } ] }, "repeat": false }""")]
    [InlineData("""{ "invoice": { "invoiceNumber": "VDT-2026-0413", "status": "Received", "approvedAmount": "x", "lines": [ { "carrierBookingReference": "R", "amount": 1, "result": "Matched" } ] }, "repeat": false }""")]
    [InlineData("""{ "invoice": { "invoiceNumber": "VDT-2026-0413", "status": "Received", "approvedAmount": null, "lines": [] }, "repeat": false }""")]
    [InlineData("""{ "invoice": { "invoiceNumber": "VDT-2026-0413", "status": "Received", "approvedAmount": null, "lines": [ { "carrierBookingReference": "R", "amount": 1, "result": "Surprise" } ] }, "repeat": false }""")]
    [InlineData("""{ "invoice": { "invoiceNumber": "VDT-2026-0413", "status": "Received", "approvedAmount": null, "lines": [ "R" ] }, "repeat": false }""")]
    public void Relay_ASuccessItCannotPassOnInFull_IsNotReceived_BecauseRelayingPartWouldChangeItsMeaning(string erpBody)
    {
        var response = Relay(201, erpBody);

        response.StatusCode.Should().Be(502);
        response.Body["result"]!.GetValue<string>().Should().Be("NOT_RECEIVED");
        response.FailureCode.Should().NotBeNull();
    }

    [Fact]
    public void Relay_WithoutAMappedInvoice_Throws()
    {
        var relay = () => CarrierInvoiceResponses.Relay(null!, 201, string.Empty, Correlation);

        relay.Should().Throw<ArgumentNullException>();
    }
}
