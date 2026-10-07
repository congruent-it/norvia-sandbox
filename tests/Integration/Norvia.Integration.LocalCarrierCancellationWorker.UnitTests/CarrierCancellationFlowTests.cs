using System.Net;
using System.Text.Json.Nodes;
using Microsoft.IdentityModel.JsonWebTokens;

namespace Norvia.Integration.LocalCarrierCancellationWorker.UnitTests;

public class CarrierCancellationFlowTests
{
    [Fact]
    public async Task RunAsync_TheCarrierAcceptsTheCancellation_ReportsItSentAndIsDone()
    {
        var world = new CarrierCancellationWorld();

        var outcome = await world.RunAsync(CarrierCancellationWorld.Cancelled());

        outcome.Should().Be(CarrierCancellationFlowOutcome.Done);
        world.Carrier.Requests.Should().ContainSingle();
        world.Tms.Requests.Should().ContainSingle().Which.Uri.AbsolutePath.Should().Be("/v1/carrier-bookings/CB-0000041/report-cancellation-told");
    }

    [Fact]
    public async Task RunAsync_SendingTheRequest_MapsItAndCarriesTheCorrelationIdAndTheCredential()
    {
        var world = new CarrierCancellationWorld();

        await world.RunAsync(CarrierCancellationWorld.Cancelled());

        var sent = world.Carrier.Requests.Single();
        sent.Uri.Should().Be(new Uri("http://carrier.test/carrier/v1/CAR-0001/cancellations"));
        sent.Headers["X-Correlation-ID"].Should().Be("cancel-1");
        sent.Headers["X-Api-Key"].Should().Be("carrier-secret");
        var cancellation = JsonNode.Parse(sent.Body)!.AsObject();
        cancellation["carrierBookingNumber"]!.GetValue<string>().Should().Be(CarrierCancellationWorld.CarrierBookingNumber);
        cancellation["kind"]!.GetValue<string>().Should().Be("CANCELLED");
        cancellation["reasonText"]!.GetValue<string>().Should().Be("The ETA is far beyond the delivery window.");
        sent.Body.Should().NotContain("EX-0000001");
    }

    [Fact]
    public async Task RunAsync_AWithdrawnBooking_IsSentWithoutAReferenceOrAReason_AndReportedTold()
    {
        var world = new CarrierCancellationWorld();

        var outcome = await world.RunAsync(CarrierCancellationWorld.Withdrawn());

        outcome.Should().Be(CarrierCancellationFlowOutcome.Done);
        var sent = world.Carrier.Requests.Single();
        sent.Headers["X-Correlation-ID"].Should().Be("withdraw-1");
        var cancellation = JsonNode.Parse(sent.Body)!.AsObject();
        cancellation["kind"]!.GetValue<string>().Should().Be("WITHDRAWN");
        cancellation.ContainsKey("carrierBookingReference").Should().BeFalse();
        cancellation.ContainsKey("reasonText").Should().BeFalse();
        world.Tms.Requests.Single().Uri.AbsolutePath.Should().Be("/v1/carrier-bookings/CB-0000041/report-cancellation-told");
    }

    [Fact]
    public async Task RunAsync_ReportingToTheTms_UsesTheActionsCorrelationIdAndTheReportRole()
    {
        var world = new CarrierCancellationWorld();

        await world.RunAsync(CarrierCancellationWorld.Cancelled());

        var report = world.Tms.Requests.Single();
        report.Headers["X-Correlation-ID"].Should().Be("cancel-1");
        var token = new JsonWebTokenHandler().ReadJsonWebToken(report.Headers["Authorization"]["Bearer ".Length..]);
        token.GetClaim("roles").Value.Should().Be("CarrierBookings.Report");
        token.GetClaim("azp").Value.Should().Be("carrier-cancellation-worker");
    }

    [Fact]
    public async Task RunAsync_AServerErrorThenSuccess_RetriesWithGrowingWaits()
    {
        var world = new CarrierCancellationWorld();
        world.Carrier.Then(HttpStatusCode.InternalServerError).Then(HttpStatusCode.BadGateway).Then(HttpStatusCode.OK);

        var outcome = await world.RunAsync(CarrierCancellationWorld.Cancelled());

        outcome.Should().Be(CarrierCancellationFlowOutcome.Done);
        var times = world.Carrier.Requests.Select(request => request.At).ToList();
        times.Should().HaveCount(3);
        (times[1] - times[0]).Should().BeGreaterThanOrEqualTo(TimeSpan.FromSeconds(5));
        (times[2] - times[1]).Should().BeGreaterThanOrEqualTo(TimeSpan.FromSeconds(10));
        world.Tms.Requests.Single().Uri.AbsolutePath.Should().EndWith("/report-cancellation-told");
    }

    [Fact]
    public async Task RunAsync_ServerErrorsEveryTime_StopsAfterTheBoundAndReportsNotDelivered()
    {
        var world = new CarrierCancellationWorld(retryCount: 3);
        world.Carrier.Otherwise(HttpStatusCode.ServiceUnavailable, "Overloaded");

        var outcome = await world.RunAsync(CarrierCancellationWorld.Cancelled());

        outcome.Should().Be(CarrierCancellationFlowOutcome.Done);
        world.Carrier.Requests.Should().HaveCount(4);
        var report = world.Tms.Requests.Single();
        report.Uri.AbsolutePath.Should().Be("/v1/carrier-bookings/CB-0000041/report-cancellation-not-delivered");
        var body = JsonNode.Parse(report.Body)!;
        body["statusCode"]!.GetValue<int>().Should().Be(503);
        body["message"]!.GetValue<string>().Should().Be("Overloaded");
    }

    [Fact]
    public async Task RunAsync_TheWaitsBetweenRetries_AreCappedAtTheMaximumInterval()
    {
        var world = new CarrierCancellationWorld(retryCount: 4);
        world.Carrier.Otherwise(HttpStatusCode.InternalServerError);

        await world.RunAsync(CarrierCancellationWorld.Cancelled());

        var times = world.Carrier.Requests.Select(request => request.At).ToList();
        (times[4] - times[3]).Should().BeLessThan(TimeSpan.FromSeconds(22));
    }

    [Fact]
    public async Task RunAsync_ATimeout_IsRetriedAndReportedWithStatusCodeZero()
    {
        var world = new CarrierCancellationWorld(retryCount: 1);
        world.Carrier.ThenFail(new TaskCanceledException("timed out")).ThenFail(new TaskCanceledException("timed out"));

        await world.RunAsync(CarrierCancellationWorld.Cancelled());

        world.Carrier.Requests.Should().HaveCount(2);
        var body = JsonNode.Parse(world.Tms.Requests.Single().Body)!;
        body["statusCode"]!.GetValue<int>().Should().Be(0);
        body["message"]!.GetValue<string>().Should().Contain("timed out");
    }

    [Fact]
    public async Task RunAsync_ACarrierThatCantBeConnectedTo_IsRetriedWithTheTechnicalCause()
    {
        var world = new CarrierCancellationWorld(retryCount: 0);
        world.Carrier.ThenFail(new HttpRequestException("Connection refused"));

        await world.RunAsync(CarrierCancellationWorld.Cancelled());

        JsonNode.Parse(world.Tms.Requests.Single().Body)!["message"]!.GetValue<string>().Should().Be("Connection refused");
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.UnprocessableEntity)]
    public async Task RunAsync_AClientError_IsNotRetriedAndReportedNotDelivered(HttpStatusCode status)
    {
        var world = new CarrierCancellationWorld();
        world.Carrier.Otherwise(status, "Refused");

        var outcome = await world.RunAsync(CarrierCancellationWorld.Cancelled());

        outcome.Should().Be(CarrierCancellationFlowOutcome.Done);
        world.Carrier.Requests.Should().ContainSingle();
        var report = world.Tms.Requests.Single();
        report.Uri.AbsolutePath.Should().EndWith("/report-cancellation-not-delivered");
        JsonNode.Parse(report.Body)!["statusCode"]!.GetValue<int>().Should().Be((int)status);
    }

    [Theory]
    [InlineData(HttpStatusCode.RequestTimeout)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    public async Task RunAsync_ARequestTimeoutOrTooManyRequests_IsRetried(HttpStatusCode status)
    {
        var world = new CarrierCancellationWorld();
        world.Carrier.Then(status).Then(HttpStatusCode.OK);

        await world.RunAsync(CarrierCancellationWorld.Cancelled());

        world.Carrier.Requests.Should().HaveCount(2);
    }

    [Fact]
    public async Task RunAsync_ALongAnswer_IsShortenedInTheReport()
    {
        var world = new CarrierCancellationWorld(retryCount: 0);
        world.Carrier.Otherwise(HttpStatusCode.BadRequest, new string('x', 2000));

        await world.RunAsync(CarrierCancellationWorld.Cancelled());

        JsonNode.Parse(world.Tms.Requests.Single().Body)!["message"]!.GetValue<string>().Should().HaveLength(500);
    }

    [Fact]
    public async Task RunAsync_AnAnswerWithoutABody_IsReportedWithItsReasonPhrase()
    {
        var world = new CarrierCancellationWorld();
        world.Carrier.Otherwise(HttpStatusCode.BadRequest);

        await world.RunAsync(CarrierCancellationWorld.Cancelled());

        JsonNode.Parse(world.Tms.Requests.Single().Body)!["message"]!.GetValue<string>().Should().Be("Bad Request");
    }

    [Fact]
    public async Task RunAsync_ACarrierWithoutAnEndpoint_ReportsNotDeliveredWithoutSending()
    {
        var world = new CarrierCancellationWorld(carrierConfigured: false);

        var outcome = await world.RunAsync(CarrierCancellationWorld.Cancelled());

        outcome.Should().Be(CarrierCancellationFlowOutcome.Done);
        world.Carrier.Requests.Should().BeEmpty();
        JsonNode.Parse(world.Tms.Requests.Single().Body)!["message"]!.GetValue<string>().Should().Contain(CarrierCancellationWorld.CarrierId);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("""{ "correlationId": "cancel-1", "carrierBookingNumber": null, "carrierId": null }""")]
    [InlineData("""{ "carrierBookingNumber": "CB-0000041", "carrierId": "CAR-0001" }""")]
    [InlineData("""["a cancellation"]""")]
    [InlineData("""{ "correlationId": "cancel-1", "carrierBookingNumber": "CB-0000041", "shipmentNumber": "SH-0000041", "carrierId": "CAR-0001", "eventType": "CarrierBookingRejected" }""")]
    [InlineData("""{ "correlationId": "cancel-1", "carrierBookingNumber": "CB-0000041", "shipmentNumber": "SH-0000041", "carrierId": "CAR-0001", "eventType": "CarrierBookingCancelled" }""")]
    public async Task RunAsync_NotACancellationTheCarrierCanBeToldOf_IsUnreadableAndCallsNobody(string message)
    {
        var world = new CarrierCancellationWorld();

        var outcome = await world.RunAsync(message);

        outcome.Should().Be(CarrierCancellationFlowOutcome.Unreadable);
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
        var world = new CarrierCancellationWorld();
        world.Tms.Otherwise(tmsStatus);

        var outcome = await world.RunAsync(CarrierCancellationWorld.Cancelled());

        outcome.ToString().Should().Be(expected);
    }

    [Fact]
    public async Task RunAsync_TheTmsCantBeReached_SaysToTryAgain()
    {
        var world = new CarrierCancellationWorld();
        world.Tms.ThenFail(new HttpRequestException("Connection refused"));

        var outcome = await world.RunAsync(CarrierCancellationWorld.Cancelled());

        outcome.Should().Be(CarrierCancellationFlowOutcome.TmsUnavailable);
    }

    [Fact]
    public async Task RunAsync_AFailedReportOfNotDelivered_StillSaysWhetherToTryAgain()
    {
        var world = new CarrierCancellationWorld();
        world.Carrier.Otherwise(HttpStatusCode.BadRequest);
        world.Tms.Otherwise(HttpStatusCode.ServiceUnavailable);

        var outcome = await world.RunAsync(CarrierCancellationWorld.Cancelled());

        outcome.Should().Be(CarrierCancellationFlowOutcome.TmsUnavailable);
    }
}
