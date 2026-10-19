using System.Net;
using System.Text.Json.Nodes;
using Microsoft.IdentityModel.JsonWebTokens;

namespace Norvia.Integration.LocalCarrierInvoiceOutcomeWorker.UnitTests;

public class CarrierInvoiceOutcomeFlowTests
{
    [Fact]
    public async Task RunAsync_TheCarrierAcknowledgesTheInvoice_ReportsItToldAndIsDone()
    {
        var world = new CarrierInvoiceOutcomeWorld();

        var outcome = await world.RunAsync(CarrierInvoiceOutcomeWorld.Approved());

        outcome.Should().Be(CarrierInvoiceOutcomeFlowOutcome.Done);
        world.Carrier.Requests.Should().ContainSingle();
        world.Erp.Requests.Should().ContainSingle().Which.Uri.AbsolutePath.Should().Be("/v1/carrier-invoices/CAR-0001/VDT-2030-0412/report-told");
    }

    [Fact]
    public async Task RunAsync_SendingTheInvoice_MapsItAndCarriesTheCorrelationIdAndTheCredential()
    {
        var world = new CarrierInvoiceOutcomeWorld();

        await world.RunAsync(CarrierInvoiceOutcomeWorld.Approved());

        var sent = world.Carrier.Requests.Single();
        sent.Uri.Should().Be(new Uri("http://carrier.test/carrier/v1/CAR-0001/invoice-outcomes"));
        sent.Headers["X-Correlation-ID"].Should().Be("carrier-invoice-1");
        sent.Headers["X-Api-Key"].Should().Be("carrier-secret");
        var invoice = JsonNode.Parse(sent.Body)!.AsObject();
        invoice["invoiceNo"]!.GetValue<string>().Should().Be("VDT-2030-0412");
        invoice["approvedAmount"]!.GetValue<decimal>().Should().Be(165.50m);
        invoice["lines"]!.AsArray().Select(line => line!["result"]!.GetValue<string>()).Should().Equal("MATCHED", "MATCHED");
        sent.Body.Should().NotContain("CAR-0001").And.NotContain("carrier-invoice-1");
    }

    [Fact]
    public async Task RunAsync_ReportingToTheErp_UsesTheInvoicesCorrelationIdAndTheReportRole()
    {
        var world = new CarrierInvoiceOutcomeWorld();

        await world.RunAsync(CarrierInvoiceOutcomeWorld.Approved());

        var report = world.Erp.Requests.Single();
        report.Headers["X-Correlation-ID"].Should().Be("carrier-invoice-1");
        var token = new JsonWebTokenHandler().ReadJsonWebToken(report.Headers["Authorization"]["Bearer ".Length..]);
        token.GetClaim("roles").Value.Should().Be("CarrierInvoices.Report");
        token.GetClaim("azp").Value.Should().Be("carrier-invoice-outcome-worker");
    }

    [Fact]
    public async Task RunAsync_AServerErrorThenSuccess_RetriesWithGrowingWaits()
    {
        var world = new CarrierInvoiceOutcomeWorld();
        world.Carrier.Then(HttpStatusCode.InternalServerError).Then(HttpStatusCode.BadGateway).Then(HttpStatusCode.OK);

        var outcome = await world.RunAsync(CarrierInvoiceOutcomeWorld.Approved());

        outcome.Should().Be(CarrierInvoiceOutcomeFlowOutcome.Done);
        var times = world.Carrier.Requests.Select(request => request.At).ToList();
        times.Should().HaveCount(3);
        (times[1] - times[0]).Should().BeGreaterThanOrEqualTo(TimeSpan.FromSeconds(5));
        (times[2] - times[1]).Should().BeGreaterThanOrEqualTo(TimeSpan.FromSeconds(10));
        world.Erp.Requests.Single().Uri.AbsolutePath.Should().EndWith("/report-told");
    }

    [Fact]
    public async Task RunAsync_ServerErrorsEveryTime_StopsAfterTheBound_AndReportsNotDeliveredWithTheCause()
    {
        var world = new CarrierInvoiceOutcomeWorld(retryCount: 3);
        world.Carrier.Otherwise(HttpStatusCode.ServiceUnavailable, "Overloaded");

        var outcome = await world.RunAsync(CarrierInvoiceOutcomeWorld.Approved());

        outcome.Should().Be(CarrierInvoiceOutcomeFlowOutcome.Done);
        world.Carrier.Requests.Should().HaveCount(4);
        var report = world.Erp.Requests.Should().ContainSingle().Subject;
        report.Uri.AbsolutePath.Should().Be("/v1/carrier-invoices/CAR-0001/VDT-2030-0412/report-not-delivered");
        report.Headers["X-Correlation-ID"].Should().Be("carrier-invoice-1");
        var body = JsonNode.Parse(report.Body)!.AsObject();
        body["statusCode"]!.GetValue<int>().Should().Be(503);
        body["message"]!.GetValue<string>().Should().Be("Overloaded");
    }

    [Fact]
    public async Task RunAsync_AClientError_IsNotRetriedAndReportsNotDelivered()
    {
        var world = new CarrierInvoiceOutcomeWorld();
        world.Carrier.Then(HttpStatusCode.BadRequest, "Invalid outcome");

        var outcome = await world.RunAsync(CarrierInvoiceOutcomeWorld.Approved());

        outcome.Should().Be(CarrierInvoiceOutcomeFlowOutcome.Done);
        world.Carrier.Requests.Should().ContainSingle();
        var report = world.Erp.Requests.Should().ContainSingle().Subject;
        report.Uri.AbsolutePath.Should().EndWith("/report-not-delivered");
        JsonNode.Parse(report.Body)!["statusCode"]!.GetValue<int>().Should().Be(400);
    }

    [Fact]
    public async Task RunAsync_NoAnswerFromTheCarrier_IsRetriedLikeAServerError()
    {
        var world = new CarrierInvoiceOutcomeWorld();
        world.Carrier.ThenFail(new HttpRequestException("connection refused")).Then(HttpStatusCode.OK);

        var outcome = await world.RunAsync(CarrierInvoiceOutcomeWorld.Approved());

        outcome.Should().Be(CarrierInvoiceOutcomeFlowOutcome.Done);
        world.Carrier.Requests.Should().HaveCount(2);
    }

    [Fact]
    public async Task RunAsync_ACarrierWithoutAnEndpoint_SendsNothingAndReportsNotDeliveredWithoutAStatus()
    {
        var world = new CarrierInvoiceOutcomeWorld(carrierConfigured: false);

        var outcome = await world.RunAsync(CarrierInvoiceOutcomeWorld.Approved());

        outcome.Should().Be(CarrierInvoiceOutcomeFlowOutcome.Done);
        world.Carrier.Requests.Should().BeEmpty();
        var body = JsonNode.Parse(world.Erp.Requests.Single().Body)!.AsObject();
        body["statusCode"].Should().BeNull();
        body["message"]!.GetValue<string>().Should().Contain("No invoice outcome endpoint is configured for Carrier CAR-0001");
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("{}")]
    [InlineData("""{"eventType":"CarrierInvoiceApproved","correlationId":"x"}""")]
    public async Task RunAsync_AMessageThatIsNoIssuedInvoice_SendsNothing(string message)
    {
        var world = new CarrierInvoiceOutcomeWorld();

        var outcome = await world.RunAsync(message);

        outcome.Should().Be(CarrierInvoiceOutcomeFlowOutcome.Unreadable);
        world.Carrier.Requests.Should().BeEmpty();
        world.Erp.Requests.Should().BeEmpty();
    }

    [Theory]
    [InlineData(HttpStatusCode.ServiceUnavailable, "ErpUnavailable")]
    [InlineData(HttpStatusCode.NotFound, "ErpRefused")]
    public async Task RunAsync_TheErpDoesNotTakeTheReport_FailsTheRun(HttpStatusCode erpStatus, string expected)
    {
        var world = new CarrierInvoiceOutcomeWorld();
        world.Erp.Otherwise(erpStatus);

        var outcome = await world.RunAsync(CarrierInvoiceOutcomeWorld.Approved());

        outcome.Should().Be(Enum.Parse<CarrierInvoiceOutcomeFlowOutcome>(expected));
    }

    [Fact]
    public async Task RunAsync_TheErpDoesNotAnswer_FailsTheRun()
    {
        var world = new CarrierInvoiceOutcomeWorld();
        world.Erp.ThenFail(new HttpRequestException("connection refused"));

        var outcome = await world.RunAsync(CarrierInvoiceOutcomeWorld.Approved());

        outcome.Should().Be(CarrierInvoiceOutcomeFlowOutcome.ErpUnavailable);
    }

    [Fact]
    public async Task RunAsync_AReportForAnInvoiceReportedAlready_IsAnAcknowledgementToo()
    {
        // The ERP answers 200 with ignored: true for a repeat; the run is done either way.
        var world = new CarrierInvoiceOutcomeWorld();
        world.Erp.Then(HttpStatusCode.OK, """{"ignored":true}""");

        var outcome = await world.RunAsync(CarrierInvoiceOutcomeWorld.Approved());

        outcome.Should().Be(CarrierInvoiceOutcomeFlowOutcome.Done);
    }

    [Theory]
    [InlineData(HttpStatusCode.ServiceUnavailable, "ErpUnavailable")]
    [InlineData(HttpStatusCode.NotFound, "ErpRefused")]
    public async Task RunAsync_TheErpDoesNotTakeTheNotDeliveredReport_FailsTheRun(HttpStatusCode erpStatus, string expected)
    {
        var world = new CarrierInvoiceOutcomeWorld();
        world.Carrier.Then(HttpStatusCode.BadRequest, "Invalid outcome");
        world.Erp.Otherwise(erpStatus);

        var outcome = await world.RunAsync(CarrierInvoiceOutcomeWorld.Approved());

        outcome.Should().Be(Enum.Parse<CarrierInvoiceOutcomeFlowOutcome>(expected));
    }

    [Fact]
    public async Task RunAsync_TheErpCannotBeReachedToReportNotDelivered_FailsTheRun()
    {
        var world = new CarrierInvoiceOutcomeWorld();
        world.Carrier.Then(HttpStatusCode.BadRequest, "Invalid outcome");
        world.Erp.ThenFail(new HttpRequestException("connection refused"));

        var outcome = await world.RunAsync(CarrierInvoiceOutcomeWorld.Approved());

        outcome.Should().Be(CarrierInvoiceOutcomeFlowOutcome.ErpUnavailable);
    }
}
