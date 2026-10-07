using System.Net;
using System.Text.Json.Nodes;
using Microsoft.IdentityModel.JsonWebTokens;

namespace Norvia.Integration.LocalShipperNotificationWorker.UnitTests;

public class ShipperNotificationFlowTests
{
    [Fact]
    public async Task RunAsync_TheShipperAcceptsTheNotification_ReportsItSentAndIsDone()
    {
        var world = new ShipperNotificationWorld();

        var outcome = await world.RunAsync(ShipperNotificationWorld.Request());

        outcome.Should().Be(ShipperNotificationFlowOutcome.Done);
        world.Shipper.Requests.Should().ContainSingle();
        world.Tms.Requests.Should().ContainSingle().Which.Uri.AbsolutePath.Should().Be("/v1/shipper-notifications/3f8a2b6c-9d41-4c7e-8a15-6b0e2d9f7c05/report-sent");
    }

    [Fact]
    public async Task RunAsync_SendingTheRequest_MapsItAndCarriesTheCorrelationIdAndTheCredential()
    {
        var world = new ShipperNotificationWorld();

        await world.RunAsync(ShipperNotificationWorld.Request());

        var sent = world.Shipper.Requests.Single();
        sent.Uri.Should().Be(new Uri("http://shipper.test/shipper/v1/SHP-0001/notifications"));
        sent.Headers["X-Correlation-ID"].Should().Be("notify-5");
        sent.Headers["X-Api-Key"].Should().Be("shipper-secret");
        var notification = JsonNode.Parse(sent.Body)!.AsObject();
        notification["notificationId"]!.GetValue<string>().Should().Be(ShipperNotificationWorld.NotificationId);
        notification["exceptionCode"]!.GetValue<string>().Should().Be("COLLECTION_FAILED");
        sent.Body.Should().NotContain("SH-0000045");
    }

    [Fact]
    public async Task RunAsync_ReportingToTheTms_UsesTheActionsCorrelationIdAndTheReportRole()
    {
        var world = new ShipperNotificationWorld();

        await world.RunAsync(ShipperNotificationWorld.Request());

        var report = world.Tms.Requests.Single();
        report.Headers["X-Correlation-ID"].Should().Be("notify-5");
        var token = new JsonWebTokenHandler().ReadJsonWebToken(report.Headers["Authorization"]["Bearer ".Length..]);
        token.GetClaim("roles").Value.Should().Be("ShipperNotifications.Report");
        token.GetClaim("azp").Value.Should().Be("shipper-notification-worker");
    }

    [Fact]
    public async Task RunAsync_AServerErrorThenSuccess_RetriesWithGrowingWaits()
    {
        var world = new ShipperNotificationWorld();
        world.Shipper.Then(HttpStatusCode.InternalServerError).Then(HttpStatusCode.BadGateway).Then(HttpStatusCode.OK);

        var outcome = await world.RunAsync(ShipperNotificationWorld.Request());

        outcome.Should().Be(ShipperNotificationFlowOutcome.Done);
        var times = world.Shipper.Requests.Select(request => request.At).ToList();
        times.Should().HaveCount(3);
        (times[1] - times[0]).Should().BeGreaterThanOrEqualTo(TimeSpan.FromSeconds(5));
        (times[2] - times[1]).Should().BeGreaterThanOrEqualTo(TimeSpan.FromSeconds(10));
        world.Tms.Requests.Single().Uri.AbsolutePath.Should().EndWith("/report-sent");
    }

    [Fact]
    public async Task RunAsync_ServerErrorsEveryTime_StopsAfterTheBoundAndReportsNotDelivered()
    {
        var world = new ShipperNotificationWorld(retryCount: 3);
        world.Shipper.Otherwise(HttpStatusCode.ServiceUnavailable, "Overloaded");

        var outcome = await world.RunAsync(ShipperNotificationWorld.Request());

        outcome.Should().Be(ShipperNotificationFlowOutcome.Done);
        world.Shipper.Requests.Should().HaveCount(4);
        var report = world.Tms.Requests.Single();
        report.Uri.AbsolutePath.Should().Be("/v1/shipper-notifications/3f8a2b6c-9d41-4c7e-8a15-6b0e2d9f7c05/report-not-delivered");
        var body = JsonNode.Parse(report.Body)!;
        body["statusCode"]!.GetValue<int>().Should().Be(503);
        body["message"]!.GetValue<string>().Should().Be("Overloaded");
    }

    [Fact]
    public async Task RunAsync_TheWaitsBetweenRetries_AreCappedAtTheMaximumInterval()
    {
        var world = new ShipperNotificationWorld(retryCount: 4);
        world.Shipper.Otherwise(HttpStatusCode.InternalServerError);

        await world.RunAsync(ShipperNotificationWorld.Request());

        var times = world.Shipper.Requests.Select(request => request.At).ToList();
        (times[4] - times[3]).Should().BeLessThan(TimeSpan.FromSeconds(22));
    }

    [Fact]
    public async Task RunAsync_ATimeout_IsRetriedAndReportedWithoutAStatusCode()
    {
        var world = new ShipperNotificationWorld(retryCount: 1);
        world.Shipper.ThenFail(new TaskCanceledException("timed out")).ThenFail(new TaskCanceledException("timed out"));

        await world.RunAsync(ShipperNotificationWorld.Request());

        world.Shipper.Requests.Should().HaveCount(2);
        var body = JsonNode.Parse(world.Tms.Requests.Single().Body)!;
        body["statusCode"].Should().BeNull();
        body["message"]!.GetValue<string>().Should().Contain("timed out");
    }

    [Fact]
    public async Task RunAsync_AShipperThatCantBeConnectedTo_IsRetriedWithTheTechnicalCause()
    {
        var world = new ShipperNotificationWorld(retryCount: 0);
        world.Shipper.ThenFail(new HttpRequestException("Connection refused"));

        await world.RunAsync(ShipperNotificationWorld.Request());

        JsonNode.Parse(world.Tms.Requests.Single().Body)!["message"]!.GetValue<string>().Should().Be("Connection refused");
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.UnprocessableEntity)]
    public async Task RunAsync_AClientError_IsNotRetriedAndReportedNotDelivered(HttpStatusCode status)
    {
        var world = new ShipperNotificationWorld();
        world.Shipper.Otherwise(status, "Refused");

        var outcome = await world.RunAsync(ShipperNotificationWorld.Request());

        outcome.Should().Be(ShipperNotificationFlowOutcome.Done);
        world.Shipper.Requests.Should().ContainSingle();
        var report = world.Tms.Requests.Single();
        report.Uri.AbsolutePath.Should().EndWith("/report-not-delivered");
        JsonNode.Parse(report.Body)!["statusCode"]!.GetValue<int>().Should().Be((int)status);
    }

    [Theory]
    [InlineData(HttpStatusCode.RequestTimeout)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    public async Task RunAsync_ARequestTimeoutOrTooManyRequests_IsRetried(HttpStatusCode status)
    {
        var world = new ShipperNotificationWorld();
        world.Shipper.Then(status).Then(HttpStatusCode.OK);

        await world.RunAsync(ShipperNotificationWorld.Request());

        world.Shipper.Requests.Should().HaveCount(2);
    }

    [Fact]
    public async Task RunAsync_ALongAnswer_IsShortenedInTheReport()
    {
        var world = new ShipperNotificationWorld(retryCount: 0);
        world.Shipper.Otherwise(HttpStatusCode.BadRequest, new string('x', 2000));

        await world.RunAsync(ShipperNotificationWorld.Request());

        JsonNode.Parse(world.Tms.Requests.Single().Body)!["message"]!.GetValue<string>().Should().HaveLength(500);
    }

    [Fact]
    public async Task RunAsync_AnAnswerWithoutABody_IsReportedWithItsReasonPhrase()
    {
        var world = new ShipperNotificationWorld();
        world.Shipper.Otherwise(HttpStatusCode.BadRequest);

        await world.RunAsync(ShipperNotificationWorld.Request());

        JsonNode.Parse(world.Tms.Requests.Single().Body)!["message"]!.GetValue<string>().Should().Be("Bad Request");
    }

    [Fact]
    public async Task RunAsync_AShipperWithoutAnEndpoint_ReportsNotDeliveredWithoutSending()
    {
        var world = new ShipperNotificationWorld(shipperConfigured: false);

        var outcome = await world.RunAsync(ShipperNotificationWorld.Request());

        outcome.Should().Be(ShipperNotificationFlowOutcome.Done);
        world.Shipper.Requests.Should().BeEmpty();
        JsonNode.Parse(world.Tms.Requests.Single().Body)!["message"]!.GetValue<string>().Should().Contain(ShipperNotificationWorld.ShipperId);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("""{ "correlationId": "notify-5", "notificationId": null, "shipperId": null }""")]
    [InlineData("""{ "notificationId": "3f8a2b6c-9d41-4c7e-8a15-6b0e2d9f7c05", "shipperId": "SHP-0001" }""")]
    [InlineData("""["a notification"]""")]
    [InlineData("""{ "correlationId": "notify-5", "notificationId": "n-1", "shipperId": "SHP-0001", "exceptionType": "Stale" }""")]
    public async Task RunAsync_NotANotificationTheShipperCanBeToldOf_IsUnreadableAndCallsNobody(string message)
    {
        var world = new ShipperNotificationWorld();

        var outcome = await world.RunAsync(message);

        outcome.Should().Be(ShipperNotificationFlowOutcome.Unreadable);
        world.Shipper.Requests.Should().BeEmpty();
        world.Tms.Requests.Should().BeEmpty();
    }

    [Theory]
    [InlineData(HttpStatusCode.ServiceUnavailable, "TmsUnavailable")]
    [InlineData(HttpStatusCode.InternalServerError, "TmsUnavailable")]
    [InlineData(HttpStatusCode.NotFound, "TmsRefused")]
    [InlineData(HttpStatusCode.Forbidden, "TmsRefused")]
    public async Task RunAsync_TheTmsDoesNotTakeTheReport_SaysWhetherToTryAgain(HttpStatusCode tmsStatus, string expected)
    {
        var world = new ShipperNotificationWorld();
        world.Tms.Otherwise(tmsStatus);

        var outcome = await world.RunAsync(ShipperNotificationWorld.Request());

        outcome.ToString().Should().Be(expected);
    }

    [Fact]
    public async Task RunAsync_TheTmsCantBeReached_SaysToTryAgain()
    {
        var world = new ShipperNotificationWorld();
        world.Tms.ThenFail(new HttpRequestException("Connection refused"));

        var outcome = await world.RunAsync(ShipperNotificationWorld.Request());

        outcome.Should().Be(ShipperNotificationFlowOutcome.TmsUnavailable);
    }

    [Fact]
    public async Task RunAsync_AFailedReportOfNotDelivered_StillSaysWhetherToTryAgain()
    {
        var world = new ShipperNotificationWorld();
        world.Shipper.Otherwise(HttpStatusCode.BadRequest);
        world.Tms.Otherwise(HttpStatusCode.ServiceUnavailable);

        var outcome = await world.RunAsync(ShipperNotificationWorld.Request());

        outcome.Should().Be(ShipperNotificationFlowOutcome.TmsUnavailable);
    }
}
