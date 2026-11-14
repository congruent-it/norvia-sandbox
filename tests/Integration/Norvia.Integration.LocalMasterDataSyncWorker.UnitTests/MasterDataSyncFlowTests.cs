using System.Net;
using System.Text.Json.Nodes;
using Microsoft.IdentityModel.JsonWebTokens;

namespace Norvia.Integration.LocalMasterDataSyncWorker.UnitTests;

public class MasterDataSyncFlowTests
{
    [Theory]
    [InlineData(HttpStatusCode.OK)]
    [InlineData(HttpStatusCode.NoContent)]
    public async Task RunAsync_TheTmsAnswers2xx_AppliedOrNot_AndTheMessageIsReplaced(HttpStatusCode status)
    {
        var world = new MasterDataSyncWorld();
        world.Tms.Otherwise(status);

        var run = await world.RunAsync(MasterDataSyncWorld.Shipper());

        run.Replaced.Should().BeTrue();
        world.Tms.Requests.Should().ContainSingle();
    }

    [Fact]
    public async Task RunAsync_PutsTheWholeRecord_WithTheCorrelationIdAndTheReplaceRole()
    {
        var world = new MasterDataSyncWorld();

        await world.RunAsync(MasterDataSyncWorld.Shipper());

        var sent = world.Tms.Requests.Single();
        sent.Method.Should().Be(HttpMethod.Put);
        sent.Uri.Should().Be(new Uri("http://tms.test/v1/shippers/SHP-0001"));
        JsonNode.Parse(sent.Body)!.ToJsonString().Should().Be("""{"name":"Brouwer Dranken BV","active":false,"revision":2}""");
        sent.Headers["X-Correlation-ID"].Should().Be("master-data-1");
        var token = new JsonWebTokenHandler().ReadJsonWebToken(sent.Headers["Authorization"]["Bearer ".Length..]);
        token.GetClaim("roles").Value.Should().Be("MasterData.Replace");
        token.GetClaim("azp").Value.Should().Be("master-data-sync-worker");
    }

    [Fact]
    public async Task RunAsync_AServerErrorThenSuccess_RetriesWithGrowingWaits()
    {
        var world = new MasterDataSyncWorld();
        world.Tms.Then(HttpStatusCode.InternalServerError).Then(HttpStatusCode.BadGateway).Then(HttpStatusCode.OK);

        var run = await world.RunAsync(MasterDataSyncWorld.Shipper());

        run.Replaced.Should().BeTrue();
        var times = world.Tms.Requests.Select(request => request.At).ToList();
        times.Should().HaveCount(3);
        (times[1] - times[0]).Should().BeGreaterThanOrEqualTo(TimeSpan.FromSeconds(5));
        (times[2] - times[1]).Should().BeGreaterThanOrEqualTo(TimeSpan.FromSeconds(10));
    }

    [Theory]
    [InlineData(HttpStatusCode.RequestTimeout)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    public async Task RunAsync_ATimeoutOrThrottle_IsRetried(HttpStatusCode status)
    {
        var world = new MasterDataSyncWorld();
        world.Tms.Then(status).Then(HttpStatusCode.OK);

        var run = await world.RunAsync(MasterDataSyncWorld.Shipper());

        run.Replaced.Should().BeTrue();
        world.Tms.Requests.Should().HaveCount(2);
    }

    [Fact]
    public async Task RunAsync_NoAnswerEveryTime_StopsAfterTheBound_AndIsDeadLetteredAsUnavailable()
    {
        var world = new MasterDataSyncWorld(retryCount: 3);
        world.Tms.ThenFail(new HttpRequestException()).ThenFail(new HttpRequestException()).ThenFail(new HttpRequestException()).ThenFail(new HttpRequestException());

        var run = await world.RunAsync(MasterDataSyncWorld.Shipper());

        world.Tms.Requests.Should().HaveCount(4);
        run.Reason.Should().Be("TmsUnavailable");
        run.Description.Should().Contain("did not answer");
    }

    [Fact]
    public async Task RunAsync_TheWaitsBetweenRetries_AreCappedAtTheMaximumInterval()
    {
        var world = new MasterDataSyncWorld(retryCount: 4);
        world.Tms.Otherwise(HttpStatusCode.InternalServerError);

        await world.RunAsync(MasterDataSyncWorld.Shipper());

        var times = world.Tms.Requests.Select(request => request.At).ToList();
        (times[4] - times[3]).Should().BeLessThan(TimeSpan.FromSeconds(22));
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.Conflict)]
    public async Task RunAsync_AClientError_IsNotRetried_AndIsDeadLetteredAsRefusedWithTheTmsMessage(HttpStatusCode status)
    {
        var world = new MasterDataSyncWorld();
        world.Tms.Otherwise(status, """{"title":"The Shipper can't be replaced.","detail":"Unreadable.","code":"MasterDataUnreadable"}""");

        var run = await world.RunAsync(MasterDataSyncWorld.Shipper());

        world.Tms.Requests.Should().ContainSingle();
        run.Reason.Should().Be("TmsCallRefused");
        run.Description.Should().Be("Unreadable.");
    }

    [Fact]
    public async Task RunAsync_ARefusalWithoutAProblem_UsesAStandardDescription()
    {
        var world = new MasterDataSyncWorld();
        world.Tms.Otherwise(HttpStatusCode.BadRequest, "not a problem");

        var run = await world.RunAsync(MasterDataSyncWorld.Shipper());

        run.Reason.Should().Be("TmsCallRefused");
        run.Description.Should().Contain("refused");
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("""{"eventType":"Vehicle","correlationId":"c","vehicle":{"id":"V","name":"N","active":true,"revision":1}}""")]
    [InlineData("""{"eventType":"Carrier","correlationId":"c","shipper":{"id":"S","name":"N","active":true,"revision":1}}""")]
    [InlineData("""{"eventType":"Carrier","correlationId":"c","carrier":{"id":"C","name":"N","active":true}}""")]
    [InlineData("""{"eventType":"Shipper","correlationId":"c","carrier":{"id":"C","name":"N","active":true,"revision":1}}""")]
    [InlineData("""{"eventType":"Shipper","shipper":{"id":"S","name":"N","active":true,"revision":1}}""")]
    [InlineData("""{"eventType":"Shipper","correlationId":"c"}""")]
    [InlineData("""{"eventType":"Shipper","correlationId":"c","shipper":{"name":"N","active":true,"revision":1}}""")]
    [InlineData("""{"eventType":"Shipper","correlationId":"c","shipper":{"id":"S","active":true,"revision":1}}""")]
    [InlineData("""{"eventType":"Shipper","correlationId":"c","shipper":{"id":"S","name":"N","revision":1}}""")]
    [InlineData("""{"eventType":"Shipper","correlationId":"c","shipper":{"id":"S","name":"N","active":true}}""")]
    public async Task RunAsync_AnUnreadableMessage_IsDeadLetteredAndNothingIsSent(string message)
    {
        var world = new MasterDataSyncWorld();

        var run = await world.RunAsync(message);

        run.Reason.Should().Be("MasterDataUnreadable");
        run.Description.Should().Contain("nothing was sent to the TMS");
        world.Tms.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task RunAsync_ACarrierEvent_PutsTheWholeCarrierToTheTmsCarrierEndpoint()
    {
        var world = new MasterDataSyncWorld();

        var run = await world.RunAsync(MasterDataSyncWorld.Carrier());

        run.Replaced.Should().BeTrue();
        var sent = world.Tms.Requests.Single();
        sent.Method.Should().Be(HttpMethod.Put);
        sent.Uri.Should().Be(new Uri("http://tms.test/v1/carriers/CAR-0001"));
        JsonNode.Parse(sent.Body)!.ToJsonString().Should().Be("""{"name":"Van Dijk Transport BV","active":true,"revision":2}""");
        sent.Headers["X-Correlation-ID"].Should().Be("master-data-2");
    }

    [Fact]
    public async Task RunAsync_ACarrierRefusedByTheTms_IsDeadLetteredAsRefused()
    {
        var world = new MasterDataSyncWorld();
        world.Tms.Otherwise(HttpStatusCode.BadRequest, """{"detail":"Unreadable."}""");

        var run = await world.RunAsync(MasterDataSyncWorld.Carrier());

        run.Reason.Should().Be("TmsCallRefused");
    }

    [Fact]
    public async Task RunAsync_AnIdWithReservedCharacters_IsEscapedInThePath()
    {
        var world = new MasterDataSyncWorld();

        await world.RunAsync(MasterDataSyncWorld.Shipper(id: "SHP 1/2"));

        world.Tms.Requests.Single().Uri.AbsolutePath.Should().Be("/v1/shippers/SHP%201%2F2");
    }

    [Fact]
    public async Task RunAsync_AMessageWithoutAnEventId_StillRuns()
    {
        var world = new MasterDataSyncWorld();
        var message = JsonNode.Parse(MasterDataSyncWorld.Shipper())!.AsObject();
        message.Remove("eventId");

        var run = await world.RunAsync(message.ToJsonString());

        run.Replaced.Should().BeTrue();
    }
}
