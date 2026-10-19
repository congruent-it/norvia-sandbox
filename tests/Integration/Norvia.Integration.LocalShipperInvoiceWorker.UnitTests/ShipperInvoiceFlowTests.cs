using System.Net;
using System.Text.Json.Nodes;
using Microsoft.IdentityModel.JsonWebTokens;

namespace Norvia.Integration.LocalShipperInvoiceWorker.UnitTests;

public class ShipperInvoiceFlowTests
{
    [Fact]
    public async Task RunAsync_TheShipperAcknowledgesTheInvoice_ReportsItSentAndIsDone()
    {
        var world = new ShipperInvoiceWorld();

        var outcome = await world.RunAsync(ShipperInvoiceWorld.Issued());

        outcome.Should().Be(ShipperInvoiceFlowOutcome.Done);
        world.Shipper.Requests.Should().ContainSingle();
        world.Erp.Requests.Should().ContainSingle().Which.Uri.AbsolutePath.Should().Be("/v1/shipper-invoices/INV-2030-000001/report-sent");
    }

    [Fact]
    public async Task RunAsync_SendingTheInvoice_MapsItAndCarriesTheCorrelationIdAndTheCredential()
    {
        var world = new ShipperInvoiceWorld();

        await world.RunAsync(ShipperInvoiceWorld.Issued());

        var sent = world.Shipper.Requests.Single();
        sent.Uri.Should().Be(new Uri("http://shipper.test/shipper/v1/SHP-0001/invoices"));
        sent.Headers["X-Correlation-ID"].Should().Be("invoice-1");
        sent.Headers["X-Api-Key"].Should().Be("shipper-secret");
        var invoice = JsonNode.Parse(sent.Body)!.AsObject();
        invoice["documentType"]!.GetValue<string>().Should().Be("INVOICE");
        invoice["documentNumber"]!.GetValue<string>().Should().Be("INV-2030-000001");
        sent.Body.Should().NotContain("SH-0000001");
    }

    [Fact]
    public async Task RunAsync_ReportingToTheErp_UsesTheInvoicesCorrelationIdAndTheReportRole()
    {
        var world = new ShipperInvoiceWorld();

        await world.RunAsync(ShipperInvoiceWorld.Issued());

        var report = world.Erp.Requests.Single();
        report.Headers["X-Correlation-ID"].Should().Be("invoice-1");
        var token = new JsonWebTokenHandler().ReadJsonWebToken(report.Headers["Authorization"]["Bearer ".Length..]);
        token.GetClaim("roles").Value.Should().Be("ShipperInvoices.Report");
        token.GetClaim("azp").Value.Should().Be("shipper-invoice-worker");
    }

    [Fact]
    public async Task RunAsync_AServerErrorThenSuccess_RetriesWithGrowingWaits()
    {
        var world = new ShipperInvoiceWorld();
        world.Shipper.Then(HttpStatusCode.InternalServerError).Then(HttpStatusCode.BadGateway).Then(HttpStatusCode.OK);

        var outcome = await world.RunAsync(ShipperInvoiceWorld.Issued());

        outcome.Should().Be(ShipperInvoiceFlowOutcome.Done);
        var times = world.Shipper.Requests.Select(request => request.At).ToList();
        times.Should().HaveCount(3);
        (times[1] - times[0]).Should().BeGreaterThanOrEqualTo(TimeSpan.FromSeconds(5));
        (times[2] - times[1]).Should().BeGreaterThanOrEqualTo(TimeSpan.FromSeconds(10));
        world.Erp.Requests.Single().Uri.AbsolutePath.Should().EndWith("/report-sent");
    }

    [Fact]
    public async Task RunAsync_ServerErrorsEveryTime_StopsAfterTheBound_AndReportsNotDeliveredWithTheCause()
    {
        var world = new ShipperInvoiceWorld(retryCount: 3);
        world.Shipper.Otherwise(HttpStatusCode.ServiceUnavailable, "Overloaded");

        var outcome = await world.RunAsync(ShipperInvoiceWorld.Issued());

        outcome.Should().Be(ShipperInvoiceFlowOutcome.Done);
        world.Shipper.Requests.Should().HaveCount(4);
        var report = world.Erp.Requests.Should().ContainSingle().Subject;
        report.Uri.AbsolutePath.Should().Be("/v1/shipper-invoices/INV-2030-000001/report-not-delivered");
        report.Headers["X-Correlation-ID"].Should().Be("invoice-1");
        var body = JsonNode.Parse(report.Body)!.AsObject();
        body["statusCode"]!.GetValue<int>().Should().Be(503);
        body["message"]!.GetValue<string>().Should().Be("Overloaded");
    }

    [Fact]
    public async Task RunAsync_AClientError_IsNotRetriedAndReportsNotDelivered()
    {
        var world = new ShipperInvoiceWorld();
        world.Shipper.Then(HttpStatusCode.BadRequest, "Invalid invoice");

        var outcome = await world.RunAsync(ShipperInvoiceWorld.Issued());

        outcome.Should().Be(ShipperInvoiceFlowOutcome.Done);
        world.Shipper.Requests.Should().ContainSingle();
        var report = world.Erp.Requests.Should().ContainSingle().Subject;
        report.Uri.AbsolutePath.Should().EndWith("/report-not-delivered");
        JsonNode.Parse(report.Body)!["statusCode"]!.GetValue<int>().Should().Be(400);
    }

    [Fact]
    public async Task RunAsync_NoAnswerFromTheShipper_IsRetriedLikeAServerError()
    {
        var world = new ShipperInvoiceWorld();
        world.Shipper.ThenFail(new HttpRequestException("connection refused")).Then(HttpStatusCode.OK);

        var outcome = await world.RunAsync(ShipperInvoiceWorld.Issued());

        outcome.Should().Be(ShipperInvoiceFlowOutcome.Done);
        world.Shipper.Requests.Should().HaveCount(2);
    }

    [Fact]
    public async Task RunAsync_AShipperWithoutAnEndpoint_SendsNothingAndReportsNotDeliveredWithoutAStatus()
    {
        var world = new ShipperInvoiceWorld(shipperConfigured: false);

        var outcome = await world.RunAsync(ShipperInvoiceWorld.Issued());

        outcome.Should().Be(ShipperInvoiceFlowOutcome.Done);
        world.Shipper.Requests.Should().BeEmpty();
        var body = JsonNode.Parse(world.Erp.Requests.Single().Body)!.AsObject();
        body["statusCode"].Should().BeNull();
        body["message"]!.GetValue<string>().Should().Contain("No invoice endpoint is configured for Shipper SHP-0001");
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("{}")]
    [InlineData("""{"eventType":"ShipperInvoiceIssued","correlationId":"x"}""")]
    public async Task RunAsync_AMessageThatIsNoIssuedInvoice_SendsNothing(string message)
    {
        var world = new ShipperInvoiceWorld();

        var outcome = await world.RunAsync(message);

        outcome.Should().Be(ShipperInvoiceFlowOutcome.Unreadable);
        world.Shipper.Requests.Should().BeEmpty();
        world.Erp.Requests.Should().BeEmpty();
    }

    [Theory]
    [InlineData(HttpStatusCode.ServiceUnavailable, "ErpUnavailable")]
    [InlineData(HttpStatusCode.NotFound, "ErpRefused")]
    public async Task RunAsync_TheErpDoesNotTakeTheReport_FailsTheRun(HttpStatusCode erpStatus, string expected)
    {
        var world = new ShipperInvoiceWorld();
        world.Erp.Otherwise(erpStatus);

        var outcome = await world.RunAsync(ShipperInvoiceWorld.Issued());

        outcome.Should().Be(Enum.Parse<ShipperInvoiceFlowOutcome>(expected));
    }

    [Fact]
    public async Task RunAsync_TheErpDoesNotAnswer_FailsTheRun()
    {
        var world = new ShipperInvoiceWorld();
        world.Erp.ThenFail(new HttpRequestException("connection refused"));

        var outcome = await world.RunAsync(ShipperInvoiceWorld.Issued());

        outcome.Should().Be(ShipperInvoiceFlowOutcome.ErpUnavailable);
    }

    [Fact]
    public async Task RunAsync_AReportForAnInvoiceReportedAlready_IsAnAcknowledgementToo()
    {
        // The ERP answers 200 with ignored: true for a repeat; the run is done either way.
        var world = new ShipperInvoiceWorld();
        world.Erp.Then(HttpStatusCode.OK, """{"ignored":true}""");

        var outcome = await world.RunAsync(ShipperInvoiceWorld.Issued());

        outcome.Should().Be(ShipperInvoiceFlowOutcome.Done);
    }

    [Theory]
    [InlineData(HttpStatusCode.ServiceUnavailable, "ErpUnavailable")]
    [InlineData(HttpStatusCode.NotFound, "ErpRefused")]
    public async Task RunAsync_TheErpDoesNotTakeTheNotDeliveredReport_FailsTheRun(HttpStatusCode erpStatus, string expected)
    {
        var world = new ShipperInvoiceWorld();
        world.Shipper.Then(HttpStatusCode.BadRequest, "Invalid invoice");
        world.Erp.Otherwise(erpStatus);

        var outcome = await world.RunAsync(ShipperInvoiceWorld.Issued());

        outcome.Should().Be(Enum.Parse<ShipperInvoiceFlowOutcome>(expected));
    }

    [Fact]
    public async Task RunAsync_TheErpCannotBeReachedToReportNotDelivered_FailsTheRun()
    {
        var world = new ShipperInvoiceWorld();
        world.Shipper.Then(HttpStatusCode.BadRequest, "Invalid invoice");
        world.Erp.ThenFail(new HttpRequestException("connection refused"));

        var outcome = await world.RunAsync(ShipperInvoiceWorld.Issued());

        outcome.Should().Be(ShipperInvoiceFlowOutcome.ErpUnavailable);
    }
}
