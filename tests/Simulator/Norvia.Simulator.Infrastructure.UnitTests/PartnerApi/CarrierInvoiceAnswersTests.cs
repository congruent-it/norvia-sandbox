using Norvia.Simulator.Domain.Log;
using Norvia.Simulator.Infrastructure.PartnerApi;

namespace Norvia.Simulator.Infrastructure.UnitTests.PartnerApi;

/// <summary>Norvia's answers to a Carrier invoice (contracts/partner-api/carrier-invoice-response.v1.schema.json).</summary>
public sealed class CarrierInvoiceAnswersTests
{
    private const string Received = """
        {"result":"RECEIVED","messageId":"m-1","invoiceNo":"VDT-SIM1-INV","repeated":false,"status":"RECEIVED","approvedAmount":null,
         "lines":[{"bookingRef":"VDT-SIM1-3","amount":100.00,"result":"MATCHED"},{"bookingRef":"VDT-SIM1-4","amount":110.00,"result":"PRICE_MISMATCH"}]}
        """;

    [Theory]
    [InlineData(201)]
    [InlineData(200)]
    public void Invoiced_AReceivedInvoice_GivesEachLinesResult(int status)
    {
        var answer = CarrierAnswers.Invoiced(status, Received, "header-1");

        answer.Outcome.Should().Be(LogOutcome.CarrierInvoiced);
        (answer.CorrelationId, answer.Status, answer.Approved, answer.Detail).Should().Be(("m-1", "RECEIVED", false, null));
        answer.Lines.Select(line => (line.BookingReference, line.Amount, line.Result, line.Matched)).Should().Equal(
            ("VDT-SIM1-3", 100.00m, "MATCHED", true), ("VDT-SIM1-4", 110.00m, "PRICE_MISMATCH", false));
    }

    [Fact]
    public void Invoiced_AnApprovedInvoice_IsApproved() =>
        CarrierAnswers.Invoiced(201, Received.Replace("\"status\":\"RECEIVED\"", "\"status\":\"APPROVED\"", StringComparison.Ordinal), null).Approved.Should().BeTrue();

    [Theory]
    [InlineData(409, "INVOICE_NUMBER_USED", "INVOICE_NUMBER_USED (409)")]
    [InlineData(422, "CARRIER_NOT_FOUND", "CARRIER_NOT_FOUND (422)")]
    [InlineData(400, "INVALID", "INVALID (400)")]
    public void Invoiced_ARefusal_GivesItsCode_NeverItsText(int status, string result, string detail)
    {
        var answer = CarrierAnswers.Invoiced(status, $$"""{"result":"{{result}}","messageId":"m-2","text":"Some text with the number VDT-SIM1-INV"}""", null);

        answer.Outcome.Should().Be(LogOutcome.CarrierRefused);
        (answer.CorrelationId, answer.Detail).Should().Be(("m-2", detail));
    }

    [Fact]
    public void Invoiced_NotReceived_IsNotReceived() =>
        CarrierAnswers.Invoiced(502, """{"result":"NOT_RECEIVED","messageId":"m-3","text":"later"}""", null).Should()
            .Match<Norvia.Simulator.Application.Ports.CarrierInvoiceAnswer>(answer => answer.Outcome == LogOutcome.CarrierNotReceived && answer.Detail == "NOT_RECEIVED (502)");

    [Theory]
    [InlineData(401)]
    [InlineData(403)]
    public void Invoiced_RefusedAtTheEdge_IsRefused(int status) =>
        CarrierAnswers.Invoiced(status, """{"statusCode":401,"message":"Access denied"}""", "edge-1").Should()
            .Match<Norvia.Simulator.Application.Ports.CarrierInvoiceAnswer>(answer => answer.Outcome == LogOutcome.CarrierRefused && answer.CorrelationId == "edge-1");

    [Fact]
    public void Invoiced_SomethingElse_IsNotReceived() =>
        CarrierAnswers.Invoiced(200, "not json", null).Outcome.Should().Be(LogOutcome.CarrierNotReceived);
}
