using System.Net;
using System.Text.Json.Nodes;
using Microsoft.IdentityModel.JsonWebTokens;

namespace Norvia.Integration.LocalBillingWorker.UnitTests;

public class BillingFlowTests
{
    [Theory]
    [InlineData(HttpStatusCode.Created)]
    [InlineData(HttpStatusCode.OK)]
    public async Task RunAsync_TheErpRecordsTheLine_OrAnswersARepeat_AndTheMessageIsRecorded(HttpStatusCode status)
    {
        var world = new BillingWorld();
        world.Erp.Otherwise(status);

        var run = await world.RunAsync(BillingWorld.Billable());

        run.Recorded.Should().BeTrue();
        world.Erp.Requests.Should().ContainSingle();
    }

    [Fact]
    public async Task RunAsync_PostsTheEventAsItIs_WithTheCorrelationIdAndTheRecordRole()
    {
        var world = new BillingWorld();
        var message = BillingWorld.Billable();

        await world.RunAsync(message);

        var sent = world.Erp.Requests.Single();
        sent.Uri.Should().Be(new Uri("http://erp.test/v1/billable-lines"));
        sent.Body.Should().Be(message);
        sent.Headers["X-Correlation-ID"].Should().Be("billing-1");
        var token = new JsonWebTokenHandler().ReadJsonWebToken(sent.Headers["Authorization"]["Bearer ".Length..]);
        token.GetClaim("roles").Value.Should().Be("BillableLines.Record");
        token.GetClaim("azp").Value.Should().Be("billing-worker");
    }

    [Fact]
    public async Task RunAsync_AServerErrorThenSuccess_RetriesWithGrowingWaits()
    {
        var world = new BillingWorld();
        world.Erp.Then(HttpStatusCode.InternalServerError).Then(HttpStatusCode.BadGateway).Then(HttpStatusCode.Created);

        var run = await world.RunAsync(BillingWorld.Billable());

        run.Recorded.Should().BeTrue();
        var times = world.Erp.Requests.Select(request => request.At).ToList();
        times.Should().HaveCount(3);
        (times[1] - times[0]).Should().BeGreaterThanOrEqualTo(TimeSpan.FromSeconds(5));
        (times[2] - times[1]).Should().BeGreaterThanOrEqualTo(TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task RunAsync_NoAnswerEveryTime_StopsAfterTheBound_AndIsDeadLetteredAsUnavailable()
    {
        var world = new BillingWorld(retryCount: 3);
        world.Erp.ThenFail(new HttpRequestException()).ThenFail(new HttpRequestException()).ThenFail(new HttpRequestException()).ThenFail(new HttpRequestException());

        var run = await world.RunAsync(BillingWorld.Billable());

        world.Erp.Requests.Should().HaveCount(4);
        run.Reason.Should().Be("ErpUnavailable");
        run.Description.Should().Contain("did not answer");
    }

    [Fact]
    public async Task RunAsync_TheWaitsBetweenRetries_AreCappedAtTheMaximumInterval()
    {
        var world = new BillingWorld(retryCount: 4);
        world.Erp.Otherwise(HttpStatusCode.InternalServerError);

        await world.RunAsync(BillingWorld.Billable());

        var times = world.Erp.Requests.Select(request => request.At).ToList();
        (times[4] - times[3]).Should().BeLessThan(TimeSpan.FromSeconds(22));
    }

    [Theory]
    [InlineData(HttpStatusCode.Conflict, "BillableLineConflict")]
    [InlineData(HttpStatusCode.UnprocessableEntity, "ShipperNotFound")]
    [InlineData(HttpStatusCode.BadRequest, "FreightChargeTotalMismatch")]
    public async Task RunAsync_AClientErrorOrConflict_IsNotRetried_AndIsDeadLetteredWithTheErpsCodeAndMessage(HttpStatusCode status, string code)
    {
        var world = new BillingWorld();
        world.Erp.Otherwise(status, $$"""{"title":"The Billable line can't be recorded.","detail":"Because of {{code}}.","code":"{{code}}"}""");

        var run = await world.RunAsync(BillingWorld.Billable());

        world.Erp.Requests.Should().ContainSingle();
        run.Reason.Should().Be(code);
        run.Description.Should().Be($"Because of {code}.");
    }

    [Fact]
    public async Task RunAsync_ARefusalWithoutACode_IsDeadLetteredAsRefused()
    {
        var world = new BillingWorld();
        world.Erp.Otherwise(HttpStatusCode.BadRequest, "not a problem");

        var run = await world.RunAsync(BillingWorld.Billable());

        run.Reason.Should().Be("ErpCallRefused");
        run.Description.Should().Contain("refused");
    }

    [Fact]
    public async Task RunAsync_ARefusalWithOnlyATitle_UsesTheTitleAsTheDescription()
    {
        var world = new BillingWorld();
        world.Erp.Otherwise(HttpStatusCode.BadRequest, """{"title":"One or more validation errors occurred."}""");

        var run = await world.RunAsync(BillingWorld.Billable());

        run.Description.Should().Be("One or more validation errors occurred.");
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("""{"eventType":"TripDispatched","correlationId":"c","transportOrderNumber":"TO-1","shipperId":"S"}""")]
    [InlineData("""{"eventType":"TransportOrderBillable","transportOrderNumber":"TO-1","shipperId":"S"}""")]
    [InlineData("""{"eventType":"TransportOrderBillable","correlationId":"c","shipperId":"S"}""")]
    [InlineData("""{"eventType":"TransportOrderBillable","correlationId":"c","transportOrderNumber":"TO-1"}""")]
    public async Task RunAsync_AnUnreadableMessage_IsDeadLetteredAndNothingIsSent(string message)
    {
        var world = new BillingWorld();

        var run = await world.RunAsync(message);

        run.Reason.Should().Be("BillableLineUnreadable");
        run.Description.Should().Contain("nothing was sent to the ERP");
        world.Erp.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task RunAsync_AMessageWithoutOptionalKeys_StillRuns()
    {
        var world = new BillingWorld();

        var run = await world.RunAsync(JsonNode.Parse(BillingWorld.Billable())!.AsObject().Also(node => { node.Remove("eventId"); node.Remove("shipmentNumber"); }).ToJsonString());

        run.Recorded.Should().BeTrue();
    }
}

internal static class JsonObjectExtensions
{
    public static JsonObject Also(this JsonObject node, Action<JsonObject> change)
    {
        change(node);
        return node;
    }
}
