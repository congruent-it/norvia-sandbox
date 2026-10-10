using System.Net;
using System.Text.Json.Nodes;
using Microsoft.IdentityModel.JsonWebTokens;

namespace Norvia.Integration.LocalTripDispatchWorker.UnitTests;

public class TripDispatchFlowTests
{
    [Fact]
    public async Task RunAsync_TheDriverAppAcceptsTheTrip_ReportsTheVersionDeliveredAndIsDone()
    {
        var world = new TripDispatchWorld();

        var outcome = await world.RunAsync(TripDispatchWorld.Dispatched(version: 2));

        outcome.Should().Be(TripDispatchFlowOutcome.Done);
        world.DriverApp.Requests.Should().ContainSingle();
        world.Tms.Requests.Should().ContainSingle().Which.Uri.AbsolutePath.Should().Be("/v1/trips/TR-0000007/versions/2/report-delivered");
    }

    [Fact]
    public async Task RunAsync_SendingTheTrip_PassesTheWholeTripOnWithTheCorrelationIdAndTheCredential()
    {
        var world = new TripDispatchWorld();

        await world.RunAsync(TripDispatchWorld.Dispatched());

        var sent = world.DriverApp.Requests.Single();
        sent.Uri.Should().Be(new Uri("http://driver-app.test/v1/trips"));
        sent.Headers["X-Correlation-ID"].Should().Be("dispatch-1");
        sent.Headers["X-Api-Key"].Should().Be("driver-app-secret");
        var trip = JsonNode.Parse(sent.Body)!.AsObject();
        trip["tripNumber"]!.GetValue<string>().Should().Be(TripDispatchWorld.TripNumber);
        trip["version"]!.GetValue<int>().Should().Be(2);
        trip["driver"]!["driverId"]!.GetValue<string>().Should().Be("DRV-0002");
    }

    [Fact]
    public async Task RunAsync_ReportingToTheTms_UsesTheActionsCorrelationIdAndTheReportRole()
    {
        var world = new TripDispatchWorld();

        await world.RunAsync(TripDispatchWorld.Dispatched());

        var report = world.Tms.Requests.Single();
        report.Headers["X-Correlation-ID"].Should().Be("dispatch-1");
        var token = new JsonWebTokenHandler().ReadJsonWebToken(report.Headers["Authorization"]["Bearer ".Length..]);
        token.GetClaim("roles").Value.Should().Be("Trips.ReportDelivery");
        token.GetClaim("azp").Value.Should().Be("trip-dispatch-worker");
    }

    [Fact]
    public async Task RunAsync_AServerErrorThenSuccess_RetriesWithGrowingWaits()
    {
        var world = new TripDispatchWorld();
        world.DriverApp.Then(HttpStatusCode.InternalServerError).Then(HttpStatusCode.BadGateway).Then(HttpStatusCode.OK);

        var outcome = await world.RunAsync(TripDispatchWorld.Dispatched());

        outcome.Should().Be(TripDispatchFlowOutcome.Done);
        var times = world.DriverApp.Requests.Select(request => request.At).ToList();
        times.Should().HaveCount(3);
        (times[1] - times[0]).Should().BeGreaterThanOrEqualTo(TimeSpan.FromSeconds(5));
        (times[2] - times[1]).Should().BeGreaterThanOrEqualTo(TimeSpan.FromSeconds(10));
        world.Tms.Requests.Single().Uri.AbsolutePath.Should().EndWith("/report-delivered");
    }

    [Fact]
    public async Task RunAsync_ServiceUnavailableEveryTime_StopsAfterTheBoundAndReportsNotDeliveredWithTheCause()
    {
        var world = new TripDispatchWorld(retryCount: 3);
        world.DriverApp.Otherwise(HttpStatusCode.ServiceUnavailable, "Driver app unavailable");

        var outcome = await world.RunAsync(TripDispatchWorld.Dispatched(version: 1));

        outcome.Should().Be(TripDispatchFlowOutcome.Done);
        world.DriverApp.Requests.Should().HaveCount(4);
        var report = world.Tms.Requests.Single();
        report.Uri.AbsolutePath.Should().Be("/v1/trips/TR-0000007/versions/1/report-not-delivered");
        report.Headers["X-Correlation-ID"].Should().Be("dispatch-1");
        JsonNode.Parse(report.Body)!["cause"]!.GetValue<string>().Should().Be("The Driver app answered 503: Driver app unavailable (4 attempts).");
    }

    [Fact]
    public async Task RunAsync_TheWaitsBetweenRetries_AreCappedAtTheMaximumInterval()
    {
        var world = new TripDispatchWorld(retryCount: 4);
        world.DriverApp.Otherwise(HttpStatusCode.InternalServerError);

        await world.RunAsync(TripDispatchWorld.Dispatched());

        var times = world.DriverApp.Requests.Select(request => request.At).ToList();
        (times[4] - times[3]).Should().BeLessThan(TimeSpan.FromSeconds(22));
    }

    [Fact]
    public async Task RunAsync_ATimeout_IsRetriedAndReportedWithTheTechnicalCause()
    {
        var world = new TripDispatchWorld(retryCount: 1);
        world.DriverApp.ThenFail(new TaskCanceledException("timed out")).ThenFail(new TaskCanceledException("timed out"));

        await world.RunAsync(TripDispatchWorld.Dispatched());

        world.DriverApp.Requests.Should().HaveCount(2);
        JsonNode.Parse(world.Tms.Requests.Single().Body)!["cause"]!.GetValue<string>().Should().Be("The Driver app didn't answer: The Driver app timed out. (2 attempts).");
    }

    [Fact]
    public async Task RunAsync_ADriverAppThatCantBeConnectedTo_IsReportedWithTheTechnicalCause()
    {
        var world = new TripDispatchWorld(retryCount: 0);
        world.DriverApp.ThenFail(new HttpRequestException("Connection refused"));

        await world.RunAsync(TripDispatchWorld.Dispatched());

        JsonNode.Parse(world.Tms.Requests.Single().Body)!["cause"]!.GetValue<string>().Should().Contain("Connection refused").And.Contain("1 attempt)");
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.UnprocessableEntity)]
    public async Task RunAsync_AClientError_IsNotRetriedAndReportedNotDelivered(HttpStatusCode status)
    {
        var world = new TripDispatchWorld();
        world.DriverApp.Otherwise(status, "Refused");

        var outcome = await world.RunAsync(TripDispatchWorld.Dispatched());

        outcome.Should().Be(TripDispatchFlowOutcome.Done);
        world.DriverApp.Requests.Should().ContainSingle();
        var report = world.Tms.Requests.Single();
        report.Uri.AbsolutePath.Should().EndWith("/report-not-delivered");
        JsonNode.Parse(report.Body)!["cause"]!.GetValue<string>().Should().StartWith($"The Driver app answered {(int)status}");
    }

    [Theory]
    [InlineData(HttpStatusCode.RequestTimeout)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    public async Task RunAsync_ARequestTimeoutOrTooManyRequests_IsRetried(HttpStatusCode status)
    {
        var world = new TripDispatchWorld();
        world.DriverApp.Then(status).Then(HttpStatusCode.OK);

        await world.RunAsync(TripDispatchWorld.Dispatched());

        world.DriverApp.Requests.Should().HaveCount(2);
    }

    [Fact]
    public async Task RunAsync_ALongAnswer_IsShortenedInTheCause()
    {
        var world = new TripDispatchWorld(retryCount: 0);
        world.DriverApp.Otherwise(HttpStatusCode.BadRequest, new string('x', 2000));

        await world.RunAsync(TripDispatchWorld.Dispatched());

        JsonNode.Parse(world.Tms.Requests.Single().Body)!["cause"]!.GetValue<string>().Length.Should().BeLessThan(600);
    }

    [Fact]
    public async Task RunAsync_AnAnswerWithoutABody_IsReportedWithItsReasonPhrase()
    {
        var world = new TripDispatchWorld();
        world.DriverApp.Otherwise(HttpStatusCode.BadRequest);

        await world.RunAsync(TripDispatchWorld.Dispatched());

        JsonNode.Parse(world.Tms.Requests.Single().Body)!["cause"]!.GetValue<string>().Should().Contain("Bad Request");
    }

    [Fact]
    public async Task RunAsync_SendingAVersionAgainAfterTheDriverAppIsBack_DeliversItOnce()
    {
        var world = new TripDispatchWorld(retryCount: 0);
        world.DriverApp.Then(HttpStatusCode.ServiceUnavailable).Then(HttpStatusCode.OK);

        await world.RunAsync(TripDispatchWorld.Dispatched(version: 1));
        await world.RunAsync(TripDispatchWorld.Dispatched(version: 1));

        world.Tms.Requests.Select(request => request.Uri.AbsolutePath).Should().Equal(
            "/v1/trips/TR-0000007/versions/1/report-not-delivered", "/v1/trips/TR-0000007/versions/1/report-delivered");
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("""["a trip"]""")]
    [InlineData("""{ "eventType": "TripDispatched", "tripNumber": "TR-0000007", "version": 1, "driver": { "driverId": "DRV-0002" } }""")]
    [InlineData("""{ "eventType": "TripDispatched", "correlationId": "dispatch-1", "version": 1, "driver": { "driverId": "DRV-0002" } }""")]
    [InlineData("""{ "eventType": "TripDispatched", "correlationId": "dispatch-1", "tripNumber": "TR-0000007", "driver": { "driverId": "DRV-0002" } }""")]
    [InlineData("""{ "eventType": "TripDispatched", "correlationId": "dispatch-1", "tripNumber": "TR-0000007", "version": 0, "driver": { "driverId": "DRV-0002" } }""")]
    [InlineData("""{ "eventType": "TripDispatched", "correlationId": "dispatch-1", "tripNumber": "TR-0000007", "version": 1 }""")]
    [InlineData("""{ "eventType": "TripNotDelivered", "correlationId": "dispatch-1", "tripNumber": "TR-0000007", "version": 1, "driver": { "driverId": "DRV-0002" } }""")]
    public async Task RunAsync_NotATripVersionTheDriverAppCanBeSent_IsUnreadableAndCallsNobody(string message)
    {
        var world = new TripDispatchWorld();

        var outcome = await world.RunAsync(message);

        outcome.Should().Be(TripDispatchFlowOutcome.Unreadable);
        world.DriverApp.Requests.Should().BeEmpty();
        world.Tms.Requests.Should().BeEmpty();
    }

    [Theory]
    [InlineData(HttpStatusCode.ServiceUnavailable, "TmsUnavailable")]
    [InlineData(HttpStatusCode.InternalServerError, "TmsUnavailable")]
    [InlineData(HttpStatusCode.NotFound, "TmsRefused")]
    [InlineData(HttpStatusCode.Forbidden, "TmsRefused")]
    public async Task RunAsync_TheTmsDoesNotTakeTheReport_SaysWhetherToTryAgain(HttpStatusCode tmsStatus, string expected)
    {
        var world = new TripDispatchWorld();
        world.Tms.Otherwise(tmsStatus);

        var outcome = await world.RunAsync(TripDispatchWorld.Dispatched());

        outcome.ToString().Should().Be(expected);
    }

    [Fact]
    public async Task RunAsync_TheTmsCantBeReached_SaysToTryAgain()
    {
        var world = new TripDispatchWorld();
        world.Tms.ThenFail(new HttpRequestException("Connection refused"));

        var outcome = await world.RunAsync(TripDispatchWorld.Dispatched());

        outcome.Should().Be(TripDispatchFlowOutcome.TmsUnavailable);
    }

    [Fact]
    public async Task RunAsync_AFailedReportOfNotDelivered_StillSaysWhetherToTryAgain()
    {
        var world = new TripDispatchWorld();
        world.DriverApp.Otherwise(HttpStatusCode.BadRequest);
        world.Tms.Otherwise(HttpStatusCode.ServiceUnavailable);

        var outcome = await world.RunAsync(TripDispatchWorld.Dispatched());

        outcome.Should().Be(TripDispatchFlowOutcome.TmsUnavailable);
    }
}
