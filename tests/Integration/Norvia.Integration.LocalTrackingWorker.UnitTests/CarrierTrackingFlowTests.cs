using System.Net;
using System.Text.Json.Nodes;
using Microsoft.IdentityModel.JsonWebTokens;
using Norvia.Integration.CarrierBooking;

namespace Norvia.Integration.LocalTrackingWorker.UnitTests;

/// <summary>The local worker settles a tracking message as the Carrier-tracking workflow does (ADR 0031).</summary>
public class CarrierTrackingFlowTests
{
    private const string Recorded = """{"shipmentNumber":"SH-0000042","shipmentStatus":"Collected","eventId":"E-1","movedShipment":true,"correlationId":"c-1","repeat":false}""";

    [Fact]
    public async Task RunAsync_TheTmsRecordsTheEvent_CompletesTheMessage()
    {
        var world = new TrackingWorld();
        world.Tms.Then(HttpStatusCode.OK, Recorded);

        var run = await world.Flow.RunAsync(TrackingWorld.Collected, TrackingWorld.Properties(), CancellationToken.None);

        run.Should().Be(new TrackingRun(TrackingSettlement.Complete));
        run.RunFailed.Should().BeFalse();
    }

    [Fact]
    public async Task RunAsync_RecordsTheMappedEventForTheCarrierOfTheMessage_NotThePayload()
    {
        var world = new TrackingWorld();
        var tracking = JsonNode.Parse(TrackingWorld.Collected)!.AsObject();
        tracking["carrierId"] = "CAR-0003";

        await world.Flow.RunAsync(tracking.ToJsonString(), TrackingWorld.Properties(), CancellationToken.None);

        var call = world.Tms.Requests.Should().ContainSingle().Subject;
        call.Uri.AbsolutePath.Should().Be("/v1/tracking-events");
        var sent = JsonNode.Parse(call.Body)!.AsObject();
        sent["source"]!["carrierId"]!.GetValue<string>().Should().Be("CAR-0001");
        sent["type"]!.GetValue<string>().Should().Be("Collected");
        call.Body.Should().NotContain("CAR-0003");
    }

    [Fact]
    public async Task RunAsync_CallsTheTmsUnderTheMessagesCorrelationId_AsTheRecordRoleIdentity()
    {
        var world = new TrackingWorld();

        await world.Flow.RunAsync(TrackingWorld.Collected, TrackingWorld.Properties(correlationId: "c-77"), CancellationToken.None);

        var call = world.Tms.Requests.Single();
        call.Headers["X-Correlation-ID"].Should().Be("c-77");
        var token = new JsonWebTokenHandler().ReadJsonWebToken(call.Headers["Authorization"]["Bearer ".Length..]);
        token.GetClaim("roles").Value.Should().Be("TrackingEvents.Record");
        token.GetClaim("azp").Value.Should().Be("tracking-worker");
    }

    [Fact]
    public async Task RunAsync_AClientError_DeadLettersWithTheFailureCodeAsReasonAndTheMessageAsDescription_WithoutRetry()
    {
        var world = new TrackingWorld();
        world.Tms.Otherwise(HttpStatusCode.NotFound, """{"code":"ShipmentNotFound","detail":"No Shipment has that booking reference."}""");

        var run = await world.Flow.RunAsync(TrackingWorld.Collected, TrackingWorld.Properties(), CancellationToken.None);

        run.Should().Be(new TrackingRun(TrackingSettlement.DeadLetter, "ShipmentNotFound", "No Shipment has that booking reference."));
        run.RunFailed.Should().BeTrue();
        world.Tms.Requests.Should().ContainSingle();
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.RequestTimeout)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    public async Task RunAsync_AServerErrorOrTimeout_AbandonsTheMessage_ForTheQueueToDeliverAgain(HttpStatusCode status)
    {
        var world = new TrackingWorld();
        world.Tms.Otherwise(status);

        var run = await world.Flow.RunAsync(TrackingWorld.Collected, TrackingWorld.Properties(), CancellationToken.None);

        run.Should().Be(new TrackingRun(TrackingSettlement.Retry));
        run.RunFailed.Should().BeTrue();
        world.Tms.Requests.Should().ContainSingle("the retries come from the queue, not from the call");
    }

    [Fact]
    public async Task RunAsync_TheTmsDoesNotAnswer_AbandonsTheMessage()
    {
        var world = new TrackingWorld();
        world.Tms.ThenFail(new HttpRequestException("connection refused"));

        var run = await world.Flow.RunAsync(TrackingWorld.Collected, TrackingWorld.Properties(), CancellationToken.None);

        run.Settlement.Should().Be(TrackingSettlement.Retry);
    }

    [Fact]
    public async Task RunAsync_AnUnmappableStatusCode_DeadLettersTheMessage_FailsTheRun_AndNeverCallsTheTms()
    {
        var world = new TrackingWorld();
        var tracking = JsonNode.Parse(TrackingWorld.Collected)!.AsObject();
        tracking["statusCode"] = "XX";

        var run = await world.Flow.RunAsync(tracking.ToJsonString(), TrackingWorld.Properties(), CancellationToken.None);

        run.Settlement.Should().Be(TrackingSettlement.DeadLetter);
        run.DeadLetterReason.Should().Be("UnmappableTrackingMessage");
        run.DeadLetterDescription.Should().NotBeNullOrWhiteSpace();
        run.RunFailed.Should().BeTrue();
        world.Tms.Requests.Should().BeEmpty();
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("")]
    public async Task RunAsync_AMessageThatIsNoTrackingMessage_IsUnmappable(string body)
    {
        var world = new TrackingWorld();

        var run = await world.Flow.RunAsync(body, TrackingWorld.Properties(), CancellationToken.None);

        run.DeadLetterReason.Should().Be("UnmappableTrackingMessage");
    }

    [Fact]
    public async Task RunAsync_AMessageWithoutACarrier_IsUnmappable()
    {
        var world = new TrackingWorld();
        var properties = TrackingWorld.Properties();
        properties.Remove("partyId");

        var run = await world.Flow.RunAsync(TrackingWorld.Collected, properties, CancellationToken.None);

        run.DeadLetterReason.Should().Be("UnmappableTrackingMessage");
        world.Tms.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task RunAsync_TracesTheRecordedEventWithTheTrackingContractsKeys()
    {
        var world = new TrackingWorld();
        world.Tms.Then(HttpStatusCode.OK, Recorded);
        var tracking = JsonNode.Parse(TrackingWorld.Collected)!.AsObject();
        tracking["carrierBookingNumber"] = "CB-0000007";

        await world.Flow.RunAsync(tracking.ToJsonString(), TrackingWorld.Properties(), CancellationToken.None);

        world.KeysOf("Succeeded").Should().BeEquivalentTo(new Dictionary<string, object?>
        {
            ["correlationId"] = "c-1",
            ["flow"] = "carrier-tracking",
            ["partyId"] = "CAR-0001",
            ["sourceMessageId"] = "E-1",
            ["shipmentNumber"] = "SH-0000042",
            ["carrierBookingNumber"] = "CB-0000007",
            ["outcome"] = "Succeeded",
        }, options => options.WithoutStrictOrdering());
    }

    [Fact]
    public async Task RunAsync_TracesADeadLetterWithItsFailureCode_AndALeavesOutKeysItDoesNotKnow()
    {
        var world = new TrackingWorld();
        world.Tms.Otherwise(HttpStatusCode.Conflict, """{"code":"ShipmentNotInTransport","detail":"x"}""");

        await world.Flow.RunAsync(TrackingWorld.Collected, TrackingWorld.Properties(sourceMessageId: null), CancellationToken.None);

        var keys = world.KeysOf("DeadLettered");
        keys["failureCode"].Should().Be("ShipmentNotInTransport");
        keys.Keys.Should().NotContain(["sourceMessageId", "shipmentNumber", "carrierBookingNumber"]);
    }

    [Fact]
    public async Task RunAsync_TracesARetryAsRetrying()
    {
        var world = new TrackingWorld();
        world.Tms.Otherwise(HttpStatusCode.BadGateway);

        await world.Flow.RunAsync(TrackingWorld.Collected, TrackingWorld.Properties(), CancellationToken.None);

        world.KeysOf("Retrying")["failureCode"].Should().Be("TmsUnavailable");
    }

    [Fact]
    public async Task RunAsync_TracesAnUnmappableMessageAsDeadLettered()
    {
        var world = new TrackingWorld();

        await world.Flow.RunAsync("not json", TrackingWorld.Properties(), CancellationToken.None);

        world.KeysOf("DeadLettered")["failureCode"].Should().Be("UnmappableTrackingMessage");
    }

    [Fact]
    public async Task RunAsync_WithoutACorrelationIdOnTheMessage_StillCallsTheTmsUnderOne()
    {
        var world = new TrackingWorld();
        var properties = TrackingWorld.Properties();
        properties.Remove("correlationId");

        await world.Flow.RunAsync(TrackingWorld.Collected, properties, CancellationToken.None);

        world.Tms.Requests.Single().Headers["X-Correlation-ID"].Should().NotBeNullOrWhiteSpace();
    }
}
