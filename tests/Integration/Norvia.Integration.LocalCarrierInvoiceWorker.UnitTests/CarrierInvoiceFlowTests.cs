using System.Net;
using System.Text.Json.Nodes;
using Microsoft.IdentityModel.JsonWebTokens;
using Norvia.Integration.TransportOrderIntake;

namespace Norvia.Integration.LocalCarrierInvoiceWorker.UnitTests;

public class CarrierInvoiceFlowTests
{
    private static readonly string Invoice = CarrierInvoiceWorld.Sample("two-lines.carrier-invoice.json");

    private static Task<BookingResponse> RunAsync(CarrierInvoiceWorld world, string? invoice = null) =>
        world.RunAsync(() => world.Flow.RunAsync(invoice ?? Invoice, "CAR-0001", CarrierInvoiceWorld.CorrelationId, CancellationToken.None));

    [Fact]
    public async Task RunAsync_PostsTheMappedSubmissionToTheErp_AsTheReceiveIdentity_WithTheCorrelationId()
    {
        var world = new CarrierInvoiceWorld();
        world.Erp.Otherwise(HttpStatusCode.Created, CarrierInvoiceWorld.ErpReceived());

        await RunAsync(world);

        var sent = world.Erp.Requests.Should().ContainSingle().Subject;
        sent.Uri.Should().Be(new Uri("http://erp.test/v1/carrier-invoices"));
        JsonNode.DeepEquals(JsonNode.Parse(sent.Body), JsonNode.Parse(CarrierInvoiceWorld.Sample("two-lines.carrier-invoice-submission.json"))!.AsObject().WithCorrelationId())
            .Should().BeTrue(sent.Body);
        sent.Headers["X-Correlation-ID"].Should().Be(CarrierInvoiceWorld.CorrelationId);
        var token = new JsonWebTokenHandler().ReadJsonWebToken(sent.Headers["Authorization"]["Bearer ".Length..]);
        token.GetClaim("roles").Value.Should().Be("CarrierInvoices.Receive");
        token.GetClaim("azp").Value.Should().Be("carrier-invoice-worker");
    }

    [Fact]
    public async Task RunAsync_TheCarrierIsTheOneOfTheToken_NotOneTheInvoiceNames()
    {
        var world = new CarrierInvoiceWorld();
        var invoice = JsonNode.Parse(Invoice)!.AsObject();
        invoice["carrierId"] = "CAR-0003";
        world.Erp.Otherwise(HttpStatusCode.Created, CarrierInvoiceWorld.ErpReceived());

        await RunAsync(world, invoice.ToJsonString());

        JsonNode.Parse(world.Erp.Requests.Single().Body)!["carrierId"]!.GetValue<string>().Should().Be("CAR-0001");
    }

    [Theory]
    [InlineData(HttpStatusCode.Created, false)]
    [InlineData(HttpStatusCode.OK, true)]
    public async Task RunAsync_TheErpReceivesIt_AnswersTheCarrierWithEachLinesResult(HttpStatusCode status, bool repeat)
    {
        var world = new CarrierInvoiceWorld();
        var erp = JsonNode.Parse(CarrierInvoiceWorld.ErpReceived("Received", "PriceMismatch"))!.AsObject();
        erp["repeat"] = repeat;
        world.Erp.Otherwise(status, erp.ToJsonString());

        var answer = await RunAsync(world);

        answer.StatusCode.Should().Be((int)status);
        answer.FailureCode.Should().BeNull();
        answer.Body["result"]!.GetValue<string>().Should().Be("RECEIVED");
        answer.Body["repeated"]!.GetValue<bool>().Should().Be(repeat);
        answer.Body["lines"]!.AsArray().Select(line => line!["result"]!.GetValue<string>()).Should().Equal("PRICE_MISMATCH", "PRICE_MISMATCH");
    }

    [Fact]
    public async Task RunAsync_AnInvoiceThatCantBeUsed_IsInvalid_AndNeverReachesTheErp()
    {
        var world = new CarrierInvoiceWorld();

        var answer = await RunAsync(world, """{ "invoiceNo": "X-1", "invoiceDate": "not a date", "lines": [] }""");

        answer.StatusCode.Should().Be(400);
        answer.Body["result"]!.GetValue<string>().Should().Be("INVALID");
        answer.FailureCode.Should().Be("CarrierInvoiceUnreadable");
        world.Erp.Requests.Should().BeEmpty();
    }

    [Theory]
    [InlineData(HttpStatusCode.Conflict, 409, "INVOICE_NUMBER_USED")]
    [InlineData(HttpStatusCode.UnprocessableEntity, 422, "CARRIER_NOT_FOUND")]
    [InlineData(HttpStatusCode.BadRequest, 400, "INVALID")]
    public async Task RunAsync_TheErpRefuses_TheCarrierGetsAStableResult_WithoutRetries(HttpStatusCode status, int expected, string result)
    {
        var world = new CarrierInvoiceWorld();
        world.Erp.Otherwise(status, """{ "title": "No.", "detail": "Refused.", "code": "SomeCode" }""");

        var answer = await RunAsync(world);

        answer.StatusCode.Should().Be(expected);
        answer.Body["result"]!.GetValue<string>().Should().Be(result);
        world.Erp.Requests.Should().ContainSingle();
    }

    [Fact]
    public async Task RunAsync_AServerErrorThenSuccess_RetriesWithGrowingWaits()
    {
        var world = new CarrierInvoiceWorld();
        world.Erp.Then(HttpStatusCode.InternalServerError).Then(HttpStatusCode.TooManyRequests).Then(HttpStatusCode.Created, CarrierInvoiceWorld.ErpReceived());

        var answer = await RunAsync(world);

        answer.StatusCode.Should().Be(201);
        var times = world.Erp.Requests.Select(request => request.At).ToList();
        times.Should().HaveCount(3);
        (times[1] - times[0]).Should().BeGreaterThanOrEqualTo(TimeSpan.FromSeconds(5));
        (times[2] - times[1]).Should().BeGreaterThanOrEqualTo(TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task RunAsync_NoAnswerEveryTime_StopsAfterTheBound_AndTheCarrierHearsItWasNotReceived()
    {
        var world = new CarrierInvoiceWorld(retryCount: 3);
        world.Erp.ThenFail(new HttpRequestException()).ThenFail(new HttpRequestException()).ThenFail(new HttpRequestException()).ThenFail(new HttpRequestException());

        var answer = await RunAsync(world);

        world.Erp.Requests.Should().HaveCount(4);
        answer.StatusCode.Should().Be(502);
        answer.Body["result"]!.GetValue<string>().Should().Be("NOT_RECEIVED");
        answer.FailureCode.Should().Be("ErpUnavailable");
    }

    [Fact]
    public async Task RunAsync_TheWaitsBetweenRetries_AreCappedAtTheMaximumInterval()
    {
        var world = new CarrierInvoiceWorld(retryCount: 4);
        world.Erp.Otherwise(HttpStatusCode.InternalServerError);

        await RunAsync(world);

        var times = world.Erp.Requests.Select(request => request.At).ToList();
        (times[4] - times[3]).Should().BeLessThan(TimeSpan.FromSeconds(22));
    }

    [Fact]
    public async Task RunAsync_AnotherClientError_IsNotRetried_AndIsNotTheCarriersFault()
    {
        var world = new CarrierInvoiceWorld();
        world.Erp.Otherwise(HttpStatusCode.Forbidden);

        var answer = await RunAsync(world);

        world.Erp.Requests.Should().ContainSingle();
        answer.StatusCode.Should().Be(502);
        answer.FailureCode.Should().Be("ErpCallRefused");
    }
}

internal static class SubmissionSampleExtensions
{
    /// <summary>The sample's submission carries a correlation ID of its own; the flow's is the one the test gave it.</summary>
    public static JsonObject WithCorrelationId(this JsonObject submission)
    {
        submission["correlationId"] = CarrierInvoiceWorld.CorrelationId;
        return submission;
    }
}
