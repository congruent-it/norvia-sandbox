using System.Net;
using System.Text.Json.Nodes;

namespace Norvia.Erp.Api.IntegrationTests.Invoicing;

/// <summary>
/// What the Shipper invoice Logic App reports on the documents it sent (story 6 and 13), and Finance's <b>Send again</b>: Sent or Not delivered, the first report
/// of a document counts and a later one is ignored, and a Not delivered document goes out once more.
/// </summary>
public sealed class DeliveryReportTests(ErpFixture fixture) : ErpTest(fixture)
{
    private static readonly DateTimeOffset LaterThatDay = new(2026, 10, 19, 14, 30, 0, TimeSpan.Zero);

    private async Task<Application.ShipperInvoices.ShipperInvoiceView> IssuedInvoiceAsync()
    {
        await RecordAsync(Brouwer, "TO-0000501", "TO-0000502");
        return await InvoiceAsync(Brouwer);
    }

    private async Task<Application.CreditNotes.CreditNoteView> IssuedCreditNoteAsync(Application.ShipperInvoices.ShipperInvoiceView invoice)
    {
        (await Finance.IssueCreditNoteAsync(invoice.InvoiceNumber, "TO-0000501", 40m, "A pallet was short.", Ct)).Refused.Should().BeFalse();
        return (await CreditNotesAsync(invoice.InvoiceNumber)).Single();
    }

    [Fact]
    public async Task ReportSent_AnIssuedInvoice_MovesItToSentWithTheTime_AndPublishesNothing()
    {
        var invoice = await IssuedInvoiceAsync();
        Now(LaterThatDay);

        using var response = await ReportInvoiceSentAsync(invoice.InvoiceNumber, invoice.CorrelationId);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await BodyOfAsync(response);
        body["status"]!.GetValue<string>().Should().Be("Sent");
        body["ignored"]!.GetValue<bool>().Should().BeFalse();
        var shown = await OpenInvoiceAsync(invoice.InvoiceNumber);
        shown.Status.Should().Be("Sent");
        shown.SentAt.Should().Be(LaterThatDay);
        shown.NotDelivered.Should().BeNull();
        (await Fixture.PublishedSettledAsync(Ct)).Select(message => message.EventType).Should().Equal("ShipperInvoiceIssued");
    }

    [Fact]
    public async Task ReportSent_ForAnInvoiceAlreadyReportedSent_IsIgnored_AndTheSentTimeStays()
    {
        var invoice = await IssuedInvoiceAsync();
        Now(LaterThatDay);
        await ReportInvoiceSentAsync(invoice.InvoiceNumber, invoice.CorrelationId);
        Now(LaterThatDay.AddHours(2));

        using var second = await ReportInvoiceSentAsync(invoice.InvoiceNumber, invoice.CorrelationId);

        second.StatusCode.Should().Be(HttpStatusCode.OK);
        (await BodyOfAsync(second))["ignored"]!.GetValue<bool>().Should().BeTrue();
        (await OpenInvoiceAsync(invoice.InvoiceNumber)).SentAt.Should().Be(LaterThatDay);
    }

    [Fact]
    public async Task ReportNotDelivered_AnIssuedInvoice_MovesItToNotDeliveredWithTheCause_AndPublishesShipperInvoiceNotDelivered()
    {
        var invoice = await IssuedInvoiceAsync();
        Now(LaterThatDay);

        using var response = await ReportInvoiceNotDeliveredAsync(invoice.InvoiceNumber, invoice.CorrelationId, 503, "The Shipper's invoice endpoint answered 503.");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await BodyOfAsync(response))["status"]!.GetValue<string>().Should().Be("NotDelivered");
        var shown = await OpenInvoiceAsync(invoice.InvoiceNumber);
        shown.Status.Should().Be("NotDelivered");
        shown.SentAt.Should().BeNull();
        shown.NotDelivered.Should().Be(new Application.ShipperInvoices.NotDeliveredView(LaterThatDay, 503, "The Shipper's invoice endpoint answered 503."));

        var message = (await PublishedAsync("ShipperInvoiceNotDelivered")).Should().ContainSingle().Subject;
        message.Topic.Should().Be("invoicing-events");
        message.CorrelationId.Should().Be(invoice.CorrelationId);
        message.Body["documentType"]!.GetValue<string>().Should().Be("INVOICE");
        message.Body["documentNumber"]!.GetValue<string>().Should().Be(invoice.InvoiceNumber);
        message.Body["shipperId"]!.GetValue<string>().Should().Be(Brouwer);
        message.Body["statusCode"]!.GetValue<int>().Should().Be(503);
        message.Body["message"]!.GetValue<string>().Should().Be("The Shipper's invoice endpoint answered 503.");
        message.Properties["correlationId"].Should().Be(invoice.CorrelationId);
        message.DocumentNumber.Should().Be(invoice.InvoiceNumber);
    }

    [Fact]
    public async Task ReportNotDelivered_AfterATimeoutWithoutAStatusCode_IsTakenWithoutOne()
    {
        var invoice = await IssuedInvoiceAsync();

        using var response = await ReportInvoiceNotDeliveredAsync(invoice.InvoiceNumber, invoice.CorrelationId, statusCode: null, message: "The Shipper's invoice endpoint didn't answer in time.");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await OpenInvoiceAsync(invoice.InvoiceNumber)).NotDelivered!.StatusCode.Should().BeNull();
        var message = (await PublishedAsync("ShipperInvoiceNotDelivered")).Should().ContainSingle().Subject;
        message.Body["statusCode"].Should().BeNull();
    }

    [Fact]
    public async Task ReportNotDelivered_ForAnInvoiceAlreadyReportedSent_IsIgnored_AndPublishesNothing()
    {
        var invoice = await IssuedInvoiceAsync();
        await ReportInvoiceSentAsync(invoice.InvoiceNumber, invoice.CorrelationId);

        using var late = await ReportInvoiceNotDeliveredAsync(invoice.InvoiceNumber, invoice.CorrelationId);

        late.StatusCode.Should().Be(HttpStatusCode.OK);
        (await BodyOfAsync(late))["ignored"]!.GetValue<bool>().Should().BeTrue();
        (await OpenInvoiceAsync(invoice.InvoiceNumber)).Status.Should().Be("Sent");
        (await PublishedAsync("ShipperInvoiceNotDelivered")).Should().BeEmpty();
    }

    [Fact]
    public async Task ReportSent_ForAnInvoiceAlreadyReportedNotDelivered_IsIgnored_AndItStaysNotDelivered()
    {
        var invoice = await IssuedInvoiceAsync();
        await ReportInvoiceNotDeliveredAsync(invoice.InvoiceNumber, invoice.CorrelationId);

        using var late = await ReportInvoiceSentAsync(invoice.InvoiceNumber, invoice.CorrelationId);

        (await BodyOfAsync(late))["ignored"]!.GetValue<bool>().Should().BeTrue();
        (await OpenInvoiceAsync(invoice.InvoiceNumber)).Status.Should().Be("NotDelivered");
    }

    [Fact]
    public async Task ReportNotDelivered_Twice_PublishesTheEventOnce()
    {
        var invoice = await IssuedInvoiceAsync();
        await ReportInvoiceNotDeliveredAsync(invoice.InvoiceNumber, invoice.CorrelationId);

        using var second = await ReportInvoiceNotDeliveredAsync(invoice.InvoiceNumber, invoice.CorrelationId);

        (await BodyOfAsync(second))["ignored"]!.GetValue<bool>().Should().BeTrue();
        (await PublishedAsync("ShipperInvoiceNotDelivered")).Should().ContainSingle();
    }

    [Fact]
    public async Task SendAgain_ANotDeliveredInvoice_PublishesShipperInvoiceIssuedOnceMore_UnderTheSameCorrelationId_AndCanThenBeReportedSent()
    {
        var invoice = await IssuedInvoiceAsync();
        await ReportInvoiceNotDeliveredAsync(invoice.InvoiceNumber, invoice.CorrelationId);

        var result = await Finance.SendAgainAsync(invoice.InvoiceNumber, Ct);

        result.Number.Should().Be(invoice.InvoiceNumber);
        var issued = await PublishedAsync("ShipperInvoiceIssued");
        issued.Should().HaveCount(2);
        issued.Select(message => message.DocumentNumber).Should().OnlyContain(number => number == invoice.InvoiceNumber);
        issued.Select(message => message.CorrelationId).Should().OnlyContain(id => id == invoice.CorrelationId);
        issued.Select(message => message.MessageId).Distinct().Should().HaveCount(2);
        issued[1].Body["invoice"]!["invoiceNumber"]!.GetValue<string>().Should().Be(invoice.InvoiceNumber);
        (await OpenInvoiceAsync(invoice.InvoiceNumber)).Status.Should().Be("Issued");

        using var sent = await ReportInvoiceSentAsync(invoice.InvoiceNumber, invoice.CorrelationId);
        (await BodyOfAsync(sent))["ignored"]!.GetValue<bool>().Should().BeFalse();
        (await OpenInvoiceAsync(invoice.InvoiceNumber)).Status.Should().Be("Sent");
    }

    [Fact]
    public async Task SendAgain_TwiceInARow_SendsOnce()
    {
        var invoice = await IssuedInvoiceAsync();
        await ReportInvoiceNotDeliveredAsync(invoice.InvoiceNumber, invoice.CorrelationId);
        await Finance.SendAgainAsync(invoice.InvoiceNumber, Ct);

        var second = await Finance.SendAgainAsync(invoice.InvoiceNumber, Ct);

        second.Refused.Should().BeTrue();
        (await PublishedAsync("ShipperInvoiceIssued")).Should().HaveCount(2);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SendAgain_AnInvoiceThatIsNotNotDelivered_IsRefused_AndPublishesNothing(bool sent)
    {
        var invoice = await IssuedInvoiceAsync();
        if (sent)
        {
            await ReportInvoiceSentAsync(invoice.InvoiceNumber, invoice.CorrelationId);
        }

        var result = await Finance.SendAgainAsync(invoice.InvoiceNumber, Ct);

        result.Refused.Should().BeTrue();
        (await PublishedAsync("ShipperInvoiceIssued")).Should().ContainSingle();
    }

    [Fact]
    public async Task SendAgain_ADocumentTheErpDoesNotHave_IsRefused()
    {
        (await Finance.SendAgainAsync("INV-2026-999999", Ct)).Refused.Should().BeTrue();
        (await Finance.SendAgainAsync("CN-2026-999999", Ct)).Refused.Should().BeTrue();
    }

    [Fact]
    public async Task ReportSent_ACreditNote_MovesItToSent_AndALateNotDeliveredReportIsIgnored()
    {
        var note = await IssuedCreditNoteAsync(await IssuedInvoiceAsync());
        Now(LaterThatDay);

        using var sent = await ReportCreditNoteSentAsync(note.CreditNoteNumber, note.CorrelationId);
        using var late = await ReportCreditNoteNotDeliveredAsync(note.CreditNoteNumber, note.CorrelationId);

        sent.StatusCode.Should().Be(HttpStatusCode.OK);
        (await BodyOfAsync(sent))["status"]!.GetValue<string>().Should().Be("Sent");
        (await BodyOfAsync(late))["ignored"]!.GetValue<bool>().Should().BeTrue();
        var shown = (await CreditNotesAsync()).Single();
        shown.Status.Should().Be("Sent");
        shown.SentAt.Should().Be(LaterThatDay);
        (await PublishedAsync("ShipperInvoiceNotDelivered")).Should().BeEmpty();
    }

    [Fact]
    public async Task ReportNotDelivered_ACreditNote_PublishesShipperInvoiceNotDeliveredWithTheCreditNoteDocumentType()
    {
        var note = await IssuedCreditNoteAsync(await IssuedInvoiceAsync());

        using var response = await ReportCreditNoteNotDeliveredAsync(note.CreditNoteNumber, note.CorrelationId, 422, "The Shipper's invoice endpoint refused the document.");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await CreditNotesAsync()).Single().NotDelivered!.StatusCode.Should().Be(422);
        var message = (await PublishedAsync("ShipperInvoiceNotDelivered")).Should().ContainSingle().Subject;
        message.CorrelationId.Should().Be(note.CorrelationId);
        message.Body["documentType"]!.GetValue<string>().Should().Be("CREDIT_NOTE");
        message.Body["documentNumber"]!.GetValue<string>().Should().Be(note.CreditNoteNumber);
        message.Body["statusCode"]!.GetValue<int>().Should().Be(422);
    }

    [Fact]
    public async Task SendAgain_ANotDeliveredCreditNote_PublishesCreditNoteIssuedOnceMore_UnderTheSameCorrelationId()
    {
        var note = await IssuedCreditNoteAsync(await IssuedInvoiceAsync());
        await ReportCreditNoteNotDeliveredAsync(note.CreditNoteNumber, note.CorrelationId);

        var result = await Finance.SendAgainAsync(note.CreditNoteNumber, Ct);

        result.Number.Should().Be(note.CreditNoteNumber);
        var issued = await PublishedAsync("CreditNoteIssued");
        issued.Should().HaveCount(2);
        issued.Select(message => message.CorrelationId).Should().OnlyContain(id => id == note.CorrelationId);
        (await CreditNotesAsync()).Single().Status.Should().Be("Issued");
        (await PublishedAsync("ShipperInvoiceIssued")).Should().ContainSingle();
    }

    [Fact]
    public async Task SendAgain_ACreditNoteThatIsNotNotDelivered_IsRefused()
    {
        var note = await IssuedCreditNoteAsync(await IssuedInvoiceAsync());

        (await Finance.SendAgainAsync(note.CreditNoteNumber, Ct)).Refused.Should().BeTrue();
        (await PublishedAsync("CreditNoteIssued")).Should().ContainSingle();
    }

    [Theory]
    [InlineData("shipper-invoices", "INV-2026-999999", "report-sent", "ShipperInvoiceNotFound")]
    [InlineData("shipper-invoices", "INV-2026-999999", "report-not-delivered", "ShipperInvoiceNotFound")]
    [InlineData("credit-notes", "CN-2026-999999", "report-sent", "CreditNoteNotFound")]
    [InlineData("credit-notes", "CN-2026-999999", "report-not-delivered", "CreditNoteNotFound")]
    public async Task Report_ForADocumentTheErpDoesNotHave_Answers404WithTheCodeThatSaysSo(string kind, string number, string outcome, string code)
    {
        using var response = await ReportAsync(kind, number, outcome, "a-correlation-id", new { statusCode = 500, message = "The endpoint failed." });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await CodeOfAsync(response)).Should().Be(code);
    }

    [Theory]
    [InlineData("report-sent")]
    [InlineData("report-not-delivered")]
    public async Task Report_WithoutACorrelationId_IsRefusedAsAnInvalidRequest_AndChangesNothing(string outcome)
    {
        var invoice = await IssuedInvoiceAsync();

        using var response = await ReportAsync("shipper-invoices", invoice.InvoiceNumber, outcome, correlationId: null, new { statusCode = 500, message = "The endpoint failed." });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await OpenInvoiceAsync(invoice.InvoiceNumber)).Status.Should().Be("Issued");
    }

    [Fact]
    public async Task ReportNotDelivered_WithoutTheCause_IsRefusedAsAnInvalidRequest_AndChangesNothing()
    {
        var invoice = await IssuedInvoiceAsync();

        using var response = await ReportAsync("shipper-invoices", invoice.InvoiceNumber, "report-not-delivered", invoice.CorrelationId, new JsonObject { ["statusCode"] = 500 });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await OpenInvoiceAsync(invoice.InvoiceNumber)).Status.Should().Be("Issued");
        (await PublishedAsync("ShipperInvoiceNotDelivered")).Should().BeEmpty();
    }
}
