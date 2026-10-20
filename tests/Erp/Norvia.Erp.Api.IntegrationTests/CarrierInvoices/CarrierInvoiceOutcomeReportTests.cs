using System.Net;

namespace Norvia.Erp.Api.IntegrationTests.CarrierInvoices;

/// <summary>
/// The Carrier invoice outcome Logic App's reports on an Approved Carrier invoice (E2, user stories 6 and 13): told records when the Carrier was told, not delivered flags
/// the invoice Carrier not told and publishes <c>CarrierInvoiceOutcomeNotDelivered</c>, a second report changes nothing, and only the Logic App's identity may report.
/// </summary>
public sealed class CarrierInvoiceOutcomeReportTests(ErpFixture fixture) : CarrierTest(fixture)
{
    private const string InvoiceNumber = "VDT-2026-0200";

    private async Task<string> ApprovedInvoiceAsync(string number = InvoiceNumber)
    {
        await RecordCostAsync("CB-0000401", "BK-A", 100m);
        var invoice = await ReceiveAsync(number, ("BK-A", 100m));
        invoice["status"]!.GetValue<string>().Should().Be("Approved");
        return invoice["correlationId"]!.GetValue<string>();
    }

    [Fact]
    public async Task ReportTold_RecordsWhenTheCarrierWasTold_AndPublishesNothing()
    {
        var correlationId = await ApprovedInvoiceAsync();
        await PublishedAsync("CarrierInvoiceApproved");

        using var response = await ReportCarrierInvoiceAsync("report-told", InvoiceNumber, correlationId);

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        var report = await BodyOfAsync(response);
        report["ignored"]!.GetValue<bool>().Should().BeFalse();
        report["toldAt"]!.GetValue<DateTimeOffset>().Should().Be(ErpFixture.DefaultNow);
        var invoice = await ShownAsync(InvoiceNumber);
        invoice.ToldAt.Should().Be(ErpFixture.DefaultNow);
        invoice.NotToldAt.Should().BeNull();
        (await Fixture.PublishedSettledAsync(Ct)).Select(message => message.EventType).Should().Equal("CarrierInvoiceApproved");
    }

    [Fact]
    public async Task ReportNotDelivered_FlagsTheInvoiceCarrierNotTold_AndPublishesCarrierInvoiceOutcomeNotDeliveredOnce()
    {
        var correlationId = await ApprovedInvoiceAsync();

        using var response = await ReportCarrierInvoiceAsync(
            "report-not-delivered", InvoiceNumber, correlationId, new { statusCode = 503, message = "The Carrier's invoice outcome endpoint answered 503." });

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        (await BodyOfAsync(response))["ignored"]!.GetValue<bool>().Should().BeFalse();
        var invoice = await ShownAsync(InvoiceNumber);
        invoice.ToldAt.Should().BeNull();
        invoice.NotToldAt.Should().Be(ErpFixture.DefaultNow);
        invoice.NotToldStatusCode.Should().Be(503);
        invoice.NotToldMessage.Should().Be("The Carrier's invoice outcome endpoint answered 503.");
        var message = (await PublishedAsync("CarrierInvoiceOutcomeNotDelivered")).Should().ContainSingle().Subject;
        message.Topic.Should().Be("invoicing-events");
        message.CorrelationId.Should().Be(correlationId);
        message.DocumentNumber.Should().Be(InvoiceNumber);
        message.Body["carrierId"]!.GetValue<string>().Should().Be(Carrier);
        message.Body["statusCode"]!.GetValue<int>().Should().Be(503);
    }

    [Fact]
    public async Task ReportNotDelivered_WithoutAStatusCode_IsAccepted_ForAFailureThatHadNone()
    {
        var correlationId = await ApprovedInvoiceAsync();

        using var response = await ReportCarrierInvoiceAsync("report-not-delivered", InvoiceNumber, correlationId, new { statusCode = (int?)null, message = "The request timed out." });

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        (await ShownAsync(InvoiceNumber)).NotToldStatusCode.Should().BeNull();
        (await PublishedAsync("CarrierInvoiceOutcomeNotDelivered")).Should().ContainSingle();
    }

    [Fact]
    public async Task ASecondReport_IsIgnored_AndChangesAndPublishesNothing()
    {
        var correlationId = await ApprovedInvoiceAsync();
        using var told = await ReportCarrierInvoiceAsync("report-told", InvoiceNumber, correlationId);

        using var told2 = await ReportCarrierInvoiceAsync("report-told", InvoiceNumber, correlationId);
        using var notDelivered = await ReportCarrierInvoiceAsync("report-not-delivered", InvoiceNumber, correlationId, new { statusCode = 503, message = "Too late." });

        told.StatusCode.Should().Be(HttpStatusCode.OK);
        foreach (var ignored in new[] { told2, notDelivered })
        {
            ignored.StatusCode.Should().Be(HttpStatusCode.OK);
            (await BodyOfAsync(ignored))["ignored"]!.GetValue<bool>().Should().BeTrue();
        }

        var invoice = await ShownAsync(InvoiceNumber);
        invoice.ToldAt.Should().Be(ErpFixture.DefaultNow);
        invoice.NotToldAt.Should().BeNull();
        (await PublishedAsync("CarrierInvoiceOutcomeNotDelivered")).Should().BeEmpty();
    }

    [Fact]
    public async Task AReportOnAnInvoiceThatIsNotApproved_IsIgnored()
    {
        var invoice = await ReceiveAsync(InvoiceNumber, ("BK-X", 50m));

        using var response = await ReportCarrierInvoiceAsync("report-not-delivered", InvoiceNumber, invoice["correlationId"]!.GetValue<string>(), new { statusCode = 503, message = "Down." });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await BodyOfAsync(response))["ignored"]!.GetValue<bool>().Should().BeTrue();
        (await ShownAsync(InvoiceNumber)).NotToldAt.Should().BeNull();
        (await PublishedAsync("CarrierInvoiceOutcomeNotDelivered")).Should().BeEmpty();
    }

    [Theory]
    [InlineData("report-told", "CAR-0001", "VDT-NOPE")]
    [InlineData("report-told", "CAR-0002", InvoiceNumber)]
    [InlineData("report-not-delivered", "CAR-0001", "VDT-NOPE")]
    [InlineData("report-not-delivered", "CAR-0002", InvoiceNumber)]
    public async Task AReportOnACarrierInvoiceTheErpDoesNotHave_IsRefusedWith404(string report, string carrier, string invoiceNumber)
    {
        var correlationId = await ApprovedInvoiceAsync();

        using var response = await ReportCarrierInvoiceAsync(report, invoiceNumber, correlationId, new { statusCode = 503, message = "Down." }, carrier);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await CodeOfAsync(response)).Should().Be("CarrierInvoiceNotFound");
    }

    [Fact]
    public async Task AReportWithoutACorrelationId_IsRefusedWith400()
    {
        await ApprovedInvoiceAsync();

        using var told = await ReportCarrierInvoiceAsync("report-told", InvoiceNumber, correlationId: null);
        using var notDelivered = await ReportCarrierInvoiceAsync("report-not-delivered", InvoiceNumber, correlationId: null, new { statusCode = 503, message = "Down." });

        told.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        notDelivered.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ShownAsync(InvoiceNumber)).ToldAt.Should().BeNull();
    }

    [Fact]
    public async Task ANotDeliveredReportWithoutAMessage_IsRefusedWith400()
    {
        var correlationId = await ApprovedInvoiceAsync();

        using var response = await ReportCarrierInvoiceAsync("report-not-delivered", InvoiceNumber, correlationId, new { statusCode = 503, message = " " });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ShownAsync(InvoiceNumber)).NotToldAt.Should().BeNull();
    }

    [Theory]
    [InlineData("report-told")]
    [InlineData("report-not-delivered")]
    public async Task ACallerWithoutTheReportRole_IsRefused_AndReportsNothing(string report)
    {
        var correlationId = await ApprovedInvoiceAsync();

        foreach (var roles in new string[][]
        {
            [ErpFixture.ReceiveCarrierInvoiceRole],
            [ErpFixture.ReportRole],
            [ErpFixture.ManageCarrierInvoiceRole],
            [ErpFixture.RecordCarrierCostRole],
        })
        {
            using var response = await ReportCarrierInvoiceAsync(report, InvoiceNumber, correlationId, new { statusCode = 503, message = "Down." }, roles: roles);
            response.StatusCode.Should().Be(HttpStatusCode.Forbidden, because: $"{string.Join(',', roles)} isn't {ErpFixture.ReportCarrierInvoiceRole}");
        }

        var invoice = await ShownAsync(InvoiceNumber);
        invoice.ToldAt.Should().BeNull();
        invoice.NotToldAt.Should().BeNull();
    }
}
