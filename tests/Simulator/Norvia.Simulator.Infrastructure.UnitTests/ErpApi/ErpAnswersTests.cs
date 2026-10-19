using Norvia.Simulator.Application.Ports;
using Norvia.Simulator.Domain.Log;
using Norvia.Simulator.Infrastructure.ErpApi;

namespace Norvia.Simulator.Infrastructure.UnitTests.ErpApi;

public sealed class ErpAnswersTests
{
    private const string Invoice = """
        {"invoice":{"invoiceNumber":"INV-2026-000001","correlationId":"corr-1","totalWithoutVat":120.00,"lines":[
          {"transportOrderNumber":"TO-0000001","shipperReference":"SIM-1-1","shipmentNumber":"SH-0000001","total":80.00},
          {"transportOrderNumber":"TO-0000002","shipperReference":"SIM-1-2","shipmentNumber":"SH-0000002","total":40.00}]}}
        """;

    [Fact]
    public void Invoiced_AnInvoiceTheErpIssued_GivesItsNumberTrailLinesAndTotal()
    {
        var answer = ErpAnswers.Invoiced(200, Invoice);

        answer.Outcome.Should().Be(LogOutcome.InvoiceIssued);
        (answer.InvoiceNumber, answer.CorrelationId, answer.TotalWithoutVat, answer.Detail).Should().Be(("INV-2026-000001", "corr-1", 120.00m, null));
        answer.Lines.Should().Equal(new InvoicedLine("TO-0000001", "SIM-1-1", 80.00m), new InvoicedLine("TO-0000002", "SIM-1-2", 40.00m));
    }

    [Fact]
    public void Invoiced_NothingWaiting_IsNothingToInvoice() =>
        ErpAnswers.Invoiced(200, """{"invoice":null}""").Should().Be(new InvoiceNowAnswer(LogOutcome.NothingToInvoice, null, null, [], null));

    [Theory]
    [InlineData(404, "ShipperNotFound")]
    [InlineData(400, "SomethingElse")]
    public void Invoiced_ARefusal_GivesItsCode_NeverItsMessage(int status, string code)
    {
        var answer = ErpAnswers.Invoiced(status, $$"""{"title":"Invoice now can't be run.","detail":"The ERP has no Shipper SHP-9999.","code":"{{code}}"}""");

        answer.Outcome.Should().Be(LogOutcome.InvoicingRefused);
        answer.Detail.Should().Be($"{code} ({status})");
    }

    [Theory]
    [InlineData(500, "")]
    [InlineData(502, "<html>Bad gateway</html>")]
    [InlineData(200, """{"invoice":{"invoiceNumber":"INV-1","lines":[]}}""")]
    [InlineData(200, "[]")]
    [InlineData(400, """{"errors":{"ShipperId":["needed"]}}""")]
    public void Invoiced_AnythingElse_IsNotReceived(int status, string body)
    {
        var answer = ErpAnswers.Invoiced(status, body);

        answer.Outcome.Should().Be(LogOutcome.InvoicingNotReceived);
        answer.Detail.Should().Contain($"{status}");
    }

    [Fact]
    public void CreditNote_OneTheErpIssued_GivesItsNumberAndTrail() =>
        ErpAnswers.CreditNote(201, """{"creditNoteNumber":"CN-2026-000001","correlationId":"corr-2","invoiceNumber":"INV-2026-000001"}""")
            .Should().Be(new CreditNoteAnswer(LogOutcome.CreditNoteIssued, "corr-2", "CN-2026-000001", null));

    [Theory]
    [InlineData(422, "CreditNoteAboveLineTotal")]
    [InlineData(404, "InvoiceLineNotFound")]
    public void CreditNote_ARefusal_GivesItsCodeAndTrail_NeverItsMessage(int status, string code)
    {
        var answer = ErpAnswers.CreditNote(status, $$"""{"detail":"The line total is 80.00","code":"{{code}}","correlationId":"corr-3"}""");

        answer.Should().Be(new CreditNoteAnswer(LogOutcome.InvoicingRefused, "corr-3", null, $"{code} ({status})"));
    }

    [Theory]
    [InlineData(200, """{"creditNoteNumber":"CN-1"}""")]
    [InlineData(500, "")]
    [InlineData(400, "not json")]
    public void CreditNote_AnythingElse_IsNotReceived(int status, string body) =>
        ErpAnswers.CreditNote(status, body).Outcome.Should().Be(LogOutcome.InvoicingNotReceived);

    [Fact]
    public void NotReceived_CarryNoTrail()
    {
        ErpAnswers.InvoiceNotReceived("down").Should().Be(new InvoiceNowAnswer(LogOutcome.InvoicingNotReceived, null, null, [], "down"));
        ErpAnswers.CreditNoteNotReceived("down").Should().Be(new CreditNoteAnswer(LogOutcome.InvoicingNotReceived, null, null, "down"));
    }
}
