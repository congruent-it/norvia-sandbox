using System.Net;
using System.Text.Json.Nodes;
using Microsoft.IdentityModel.JsonWebTokens;

namespace Norvia.Integration.LocalBookingWorker.UnitTests;

public class CarrierBookingFlowTests
{
    [Fact]
    public async Task RunAsync_TheCarrierAcceptsTheRequest_ReportsItSentAndIsDone()
    {
        var world = new BookingWorld();

        var outcome = await world.RunAsync(BookingWorld.Release());

        outcome.Should().Be(BookingFlowOutcome.Done);
        world.Carrier.Requests.Should().ContainSingle();
        world.Tms.Requests.Should().ContainSingle().Which.Uri.AbsolutePath.Should().Be("/v1/carrier-bookings/CB-0000001/report-sent");
    }

    [Fact]
    public async Task RunAsync_SendingTheRequest_MapsItAndCarriesTheCorrelationIdAndTheCredential()
    {
        var world = new BookingWorld();

        await world.RunAsync(BookingWorld.Release());

        var sent = world.Carrier.Requests.Single();
        sent.Uri.Should().Be(new Uri("http://carrier.test/carrier/v1/bookings"));
        sent.Headers["X-Correlation-ID"].Should().Be("release-1");
        sent.Headers["X-Api-Key"].Should().Be("carrier-secret");
        var booking = JsonNode.Parse(sent.Body)!.AsObject();
        booking["carrierBookingNumber"]!.GetValue<string>().Should().Be("CB-0000001");
        booking["collectionCountry"]!.GetValue<string>().Should().Be("NLD");
        sent.Body.Should().NotContain("SHIPPER-ACME");
    }

    [Fact]
    public async Task RunAsync_ReportingToTheTms_UsesTheReleasesCorrelationIdAndTheReportRole()
    {
        var world = new BookingWorld();

        await world.RunAsync(BookingWorld.Release());

        var report = world.Tms.Requests.Single();
        report.Headers["X-Correlation-ID"].Should().Be("release-1");
        var token = new JsonWebTokenHandler().ReadJsonWebToken(report.Headers["Authorization"]["Bearer ".Length..]);
        token.GetClaim("roles").Value.Should().Be("CarrierBookings.Report");
        token.GetClaim("azp").Value.Should().Be("booking-worker");
    }

    [Fact]
    public async Task RunAsync_AServerErrorThenSuccess_RetriesWithGrowingWaits()
    {
        var world = new BookingWorld();
        world.Carrier.Then(HttpStatusCode.InternalServerError).Then(HttpStatusCode.BadGateway).Then(HttpStatusCode.OK);

        var outcome = await world.RunAsync(BookingWorld.Release());

        outcome.Should().Be(BookingFlowOutcome.Done);
        var times = world.Carrier.Requests.Select(request => request.At).ToList();
        times.Should().HaveCount(3);
        (times[1] - times[0]).Should().BeGreaterThanOrEqualTo(TimeSpan.FromSeconds(5));
        (times[2] - times[1]).Should().BeGreaterThanOrEqualTo(TimeSpan.FromSeconds(10));
        world.Tms.Requests.Single().Uri.AbsolutePath.Should().EndWith("/report-sent");
    }

    [Fact]
    public async Task RunAsync_ServerErrorsEveryTime_StopsAfterTheBoundAndReportsNotDelivered()
    {
        var world = new BookingWorld(retryCount: 3);
        world.Carrier.Otherwise(HttpStatusCode.ServiceUnavailable, "Overloaded");

        var outcome = await world.RunAsync(BookingWorld.Release());

        outcome.Should().Be(BookingFlowOutcome.Done);
        world.Carrier.Requests.Should().HaveCount(4);
        var report = world.Tms.Requests.Single();
        report.Uri.AbsolutePath.Should().Be("/v1/carrier-bookings/CB-0000001/report-not-delivered");
        var body = JsonNode.Parse(report.Body)!;
        body["statusCode"]!.GetValue<int>().Should().Be(503);
        body["message"]!.GetValue<string>().Should().Be("Overloaded");
    }

    [Fact]
    public async Task RunAsync_TheWaitsBetweenRetries_AreCappedAtTheMaximumInterval()
    {
        var world = new BookingWorld(retryCount: 4);
        world.Carrier.Otherwise(HttpStatusCode.InternalServerError);

        await world.RunAsync(BookingWorld.Release());

        var times = world.Carrier.Requests.Select(request => request.At).ToList();
        (times[4] - times[3]).Should().BeLessThan(TimeSpan.FromSeconds(22));
    }

    [Fact]
    public async Task RunAsync_ATimeout_IsRetriedAndReportedWithStatusZero()
    {
        var world = new BookingWorld(retryCount: 1);
        world.Carrier.ThenFail(new TaskCanceledException("timed out")).ThenFail(new TaskCanceledException("timed out"));

        await world.RunAsync(BookingWorld.Release());

        world.Carrier.Requests.Should().HaveCount(2);
        var body = JsonNode.Parse(world.Tms.Requests.Single().Body)!;
        body["statusCode"]!.GetValue<int>().Should().Be(0);
        body["message"]!.GetValue<string>().Should().Contain("timed out");
    }

    [Fact]
    public async Task RunAsync_ACarrierThatCantBeConnectedTo_IsRetriedWithTheTechnicalCause()
    {
        var world = new BookingWorld(retryCount: 0);
        world.Carrier.ThenFail(new HttpRequestException("Connection refused"));

        await world.RunAsync(BookingWorld.Release());

        JsonNode.Parse(world.Tms.Requests.Single().Body)!["message"]!.GetValue<string>().Should().Be("Connection refused");
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.UnprocessableEntity)]
    public async Task RunAsync_AClientError_IsNotRetriedAndReportedNotDelivered(HttpStatusCode status)
    {
        var world = new BookingWorld();
        world.Carrier.Otherwise(status, "Refused");

        var outcome = await world.RunAsync(BookingWorld.Release());

        outcome.Should().Be(BookingFlowOutcome.Done);
        world.Carrier.Requests.Should().ContainSingle();
        var report = world.Tms.Requests.Single();
        report.Uri.AbsolutePath.Should().EndWith("/report-not-delivered");
        JsonNode.Parse(report.Body)!["statusCode"]!.GetValue<int>().Should().Be((int)status);
    }

    [Theory]
    [InlineData(HttpStatusCode.RequestTimeout)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    public async Task RunAsync_ARequestTimeoutOrTooManyRequests_IsRetried(HttpStatusCode status)
    {
        var world = new BookingWorld();
        world.Carrier.Then(status).Then(HttpStatusCode.OK);

        await world.RunAsync(BookingWorld.Release());

        world.Carrier.Requests.Should().HaveCount(2);
    }

    [Fact]
    public async Task RunAsync_ALongAnswer_IsShortenedInTheReport()
    {
        var world = new BookingWorld(retryCount: 0);
        world.Carrier.Otherwise(HttpStatusCode.BadRequest, new string('x', 2000));

        await world.RunAsync(BookingWorld.Release());

        JsonNode.Parse(world.Tms.Requests.Single().Body)!["message"]!.GetValue<string>().Should().HaveLength(500);
    }

    [Fact]
    public async Task RunAsync_AnAnswerWithoutABody_IsReportedWithItsReasonPhrase()
    {
        var world = new BookingWorld();
        world.Carrier.Otherwise(HttpStatusCode.BadRequest);

        await world.RunAsync(BookingWorld.Release());

        JsonNode.Parse(world.Tms.Requests.Single().Body)!["message"]!.GetValue<string>().Should().Be("Bad Request");
    }

    [Fact]
    public async Task RunAsync_ACarrierWithoutAnEndpoint_ReportsNotDeliveredWithoutSending()
    {
        var world = new BookingWorld(carrierConfigured: false);

        var outcome = await world.RunAsync(BookingWorld.Release());

        outcome.Should().Be(BookingFlowOutcome.Done);
        world.Carrier.Requests.Should().BeEmpty();
        JsonNode.Parse(world.Tms.Requests.Single().Body)!["message"]!.GetValue<string>().Should().Contain(BookingWorld.CarrierId);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("""{ "correlationId": "release-1", "carrierBookingNumber": null, "carrierId": null }""")]
    [InlineData("""{ "carrierBookingNumber": "CB-0000001", "carrierId": "CARRIER-VAN-DIJK" }""")]
    [InlineData("""["a release"]""")]
    public async Task RunAsync_NotACarrierRelease_IsUnreadableAndCallsNobody(string message)
    {
        var world = new BookingWorld();

        var outcome = await world.RunAsync(message);

        outcome.Should().Be(BookingFlowOutcome.Unreadable);
        world.Carrier.Requests.Should().BeEmpty();
        world.Tms.Requests.Should().BeEmpty();
    }

    [Theory]
    [InlineData(HttpStatusCode.ServiceUnavailable, "TmsUnavailable")]
    [InlineData(HttpStatusCode.InternalServerError, "TmsUnavailable")]
    [InlineData(HttpStatusCode.NotFound, "TmsRefused")]
    [InlineData(HttpStatusCode.Forbidden, "TmsRefused")]
    public async Task RunAsync_TheTmsDoesNotTakeTheReport_SaysWhetherToTryAgain(HttpStatusCode tmsStatus, string expected)
    {
        var world = new BookingWorld();
        world.Tms.Otherwise(tmsStatus);

        var outcome = await world.RunAsync(BookingWorld.Release());

        outcome.ToString().Should().Be(expected);
    }

    [Fact]
    public async Task RunAsync_TheTmsCantBeReached_SaysToTryAgain()
    {
        var world = new BookingWorld();
        world.Tms.ThenFail(new HttpRequestException("Connection refused"));

        var outcome = await world.RunAsync(BookingWorld.Release());

        outcome.Should().Be(BookingFlowOutcome.TmsUnavailable);
    }

    [Fact]
    public async Task RunAsync_AFailedReportOfNotDelivered_StillSaysWhetherToTryAgain()
    {
        var world = new BookingWorld();
        world.Carrier.Otherwise(HttpStatusCode.BadRequest);
        world.Tms.Otherwise(HttpStatusCode.ServiceUnavailable);

        var outcome = await world.RunAsync(BookingWorld.Release());

        outcome.Should().Be(BookingFlowOutcome.TmsUnavailable);
    }
}
