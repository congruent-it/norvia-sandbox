using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using Json.Schema;

namespace Norvia.Integration.LocalDriverReportsWorker.UnitTests;

/// <summary>
/// The P12 Driver reports Logic App's steps, played locally (ADR 0016): call the TMS for the Driver's report under the bounded exponential retry policy,
/// settle the message (completed, or dead-lettered with the failure code and message), then tell the Driver app what became of it.
/// </summary>
public sealed class DriverReportsFlowTests
{
    // A schema registers itself under its $id, so each is read once.
    private static readonly Lazy<JsonSchema> TrackingEventSchema = new(() => JsonSchema.FromText(ReadResource("CanonicalModel/tracking-event.v1.schema.json")));
    private static readonly Lazy<JsonSchema> StopReportedSchema = new(() => JsonSchema.FromText(ReadResource("CanonicalModel/stop-completed-reported.v1.schema.json")));

    private readonly DriverReportsWorld _world = new();

    [Fact]
    public async Task ARecordedStart_CompletesTheMessage_ThenReportsRecordedToTheDriverApp()
    {
        var outcome = await _world.RunAsync(DriverReportsWorld.StartReported());

        outcome.Should().Be(DriverReportsRunOutcome.Done);
        _world.Settled.Should().ContainSingle().Which.Settlement.Should().Be(Settlement.Complete);
        _world.Settled.Single().DriverAppCalls.Should().Be(0, "the message is settled before the outcome is reported");
        var reported = _world.OutcomeReported();
        reported["outcome"]!.GetValue<string>().Should().Be("Recorded");
        reported["driverAppEventId"]!.GetValue<string>().Should().Be(DriverReportsWorld.EventId);
        reported["tripNumber"]!.GetValue<string>().Should().Be(DriverReportsWorld.TripNumber);
        reported["correlationId"]!.GetValue<string>().Should().Be(DriverReportsWorld.CorrelationId);
        reported.ContainsKey("failureCode").Should().BeFalse();
    }

    [Fact]
    public async Task TheTmsStart_IsCalledWithTheDriverIdTheEventIdTheCorrelationIdAndTheStartRole()
    {
        await _world.RunAsync(DriverReportsWorld.StartReported());

        var call = _world.Tms.Requests.Should().ContainSingle().Subject;
        call.Uri.ToString().Should().Be("http://tms.test/v1/trips/TR-0000012/start");
        call.Headers["X-Correlation-ID"].Should().Be(DriverReportsWorld.CorrelationId);
        DriverReportsWorld.RoleOf(call).Should().Be("Trips.Start");
        var body = JsonNode.Parse(call.Body)!;
        body["driverId"]!.GetValue<string>().Should().Be("DRV-0002", "the Driver ID is the one the Driver app took from the Driver's token");
        body["driverAppEventId"]!.GetValue<string>().Should().Be(DriverReportsWorld.EventId);
    }

    [Fact]
    public async Task TheOutcomeReport_GoesToTheDriverAppsOutcomeEndpoint_AsTheIdentityHoldingTheOutcomeRole()
    {
        await _world.RunAsync(DriverReportsWorld.StartReported());

        var call = _world.DriverApp.Requests.Should().ContainSingle().Subject;
        call.Uri.ToString().Should().Be("http://driver-app.test/v1/report-outcomes");
        call.Headers["X-Correlation-ID"].Should().Be(DriverReportsWorld.CorrelationId);
        DriverReportsWorld.RoleOf(call).Should().Be("DriverReports.ReportOutcome");
    }

    [Theory]
    [InlineData(HttpStatusCode.Conflict, "NotTripDay", "Trip TR-0000012 is for Tuesday 13 October 2026, and today is Monday 12 October 2026.")]
    [InlineData(HttpStatusCode.Conflict, "TripNotDispatched", "Trip TR-0000012 is In progress.")]
    [InlineData(HttpStatusCode.NotFound, "TripNotFound", "There is no Trip TR-0000012.")]
    public async Task ARefusal_IsNotRetried_DeadLettersTheMessageWithTheTmsCodeAndMessage_AndIsReportedRefused(HttpStatusCode status, string code, string message)
    {
        _world.Tms.Otherwise(status, DriverReportsWorld.Problem((int)status, code, message));

        var outcome = await _world.RunAsync(DriverReportsWorld.StartReported());

        outcome.Should().Be(DriverReportsRunOutcome.Done);
        _world.Tms.Requests.Should().ContainSingle("a refusal won't change by sending it again");
        _world.Settled.Should().ContainSingle().Which.Settlement.Should().Be(new Settlement.DeadLetter(code, message));
        var reported = _world.OutcomeReported();
        reported["outcome"]!.GetValue<string>().Should().Be("Refused");
        reported["failureCode"]!.GetValue<string>().Should().Be(code);
        reported["message"]!.GetValue<string>().Should().Be(message);
    }

    [Fact]
    public async Task AClientErrorWithoutACode_IsTmsCallRefused_WithWhatTheTmsSaid()
    {
        _world.Tms.Otherwise(HttpStatusCode.BadRequest, """{ "title": "One or more validation errors occurred.", "status": 400 }""");

        await _world.RunAsync(DriverReportsWorld.StartReported());

        _world.Settled.Single().Settlement.Should().Be(new Settlement.DeadLetter("TmsCallRefused", "One or more validation errors occurred."));
        _world.OutcomeReported()["outcome"]!.GetValue<string>().Should().Be("Refused");
    }

    [Fact]
    public async Task AServerError_IsRetriedWithBoundedExponentialBackoff_ThenDeadLettered_AndReportedNotRecorded()
    {
        _world.Tms.Otherwise(HttpStatusCode.ServiceUnavailable, "Service Unavailable");

        var outcome = await _world.RunAsync(DriverReportsWorld.StartReported());

        outcome.Should().Be(DriverReportsRunOutcome.Done);
        _world.Tms.Requests.Should().HaveCount(4, "the first attempt and three retries");
        _world.Tms.Requests.Zip(_world.Tms.Requests.Skip(1), (earlier, later) => later.At - earlier.At).Should().BeEquivalentTo(
            new[] { TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(20) }, options => options.WithStrictOrdering().Using<TimeSpan>(context => context.Subject.Should().BeCloseTo(context.Expectation, TimeSpan.FromSeconds(1))).WhenTypeIs<TimeSpan>());
        _world.Settled.Should().ContainSingle().Which.Settlement.Should().BeOfType<Settlement.DeadLetter>().Which.Reason.Should().Be("TmsUnavailable");
        var reported = _world.OutcomeReported();
        reported["outcome"]!.GetValue<string>().Should().Be("NotRecorded");
        reported["failureCode"]!.GetValue<string>().Should().Be("TmsUnavailable");
        reported["message"]!.GetValue<string>().Should().Contain("503").And.Contain("4 attempts");
    }

    [Fact]
    public async Task AServerErrorThatPasses_IsRecorded_WithoutBeingDeadLettered()
    {
        _world.Tms.Then(HttpStatusCode.BadGateway).Then(HttpStatusCode.OK);

        await _world.RunAsync(DriverReportsWorld.StartReported());

        _world.Tms.Requests.Should().HaveCount(2);
        _world.Settled.Single().Settlement.Should().Be(Settlement.Complete);
        _world.OutcomeReported()["outcome"]!.GetValue<string>().Should().Be("Recorded");
    }

    [Theory]
    [InlineData(HttpStatusCode.RequestTimeout)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    public async Task ATimeoutAnd429_AreRetriedToo(HttpStatusCode status)
    {
        _world.Tms.Then(status).Then(HttpStatusCode.OK);

        await _world.RunAsync(DriverReportsWorld.StartReported());

        _world.Tms.Requests.Should().HaveCount(2);
    }

    [Fact]
    public async Task ATmsThatDoesNotAnswer_IsRetried_ThenReportedNotRecorded_WithTheCause()
    {
        _world.Tms.ThenFail(new HttpRequestException("Connection refused")).ThenFail(new TaskCanceledException()).ThenFail(new HttpRequestException("x")).ThenFail(new HttpRequestException("y"));

        await _world.RunAsync(DriverReportsWorld.StartReported());

        _world.Tms.Requests.Should().HaveCount(4);
        _world.OutcomeReported()["message"]!.GetValue<string>().Should().Contain("didn't answer").And.Contain("4 attempts");
    }

    [Fact]
    public async Task ARepeatedStart_TheTmsRecognises_IsRecordedAsAny()
    {
        _world.Tms.Otherwise(HttpStatusCode.OK, """{ "number": "TR-0000012", "version": 2, "correlationId": "x", "repeat": true }""");

        await _world.RunAsync(DriverReportsWorld.StartReported());

        _world.Settled.Single().Settlement.Should().Be(Settlement.Complete);
        _world.OutcomeReported()["outcome"]!.GetValue<string>().Should().Be("Recorded");
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("""{ "eventType": "TripStartReported", "tripNumber": "TR-0000012", "driverId": "DRV-0002" }""")]
    [InlineData("""{ "eventId": "0198e7c0-0000-7000-8000-0000000000c1", "eventType": "SomethingElse", "correlationId": "c", "tripNumber": "TR-0000012", "driverId": "DRV-0002" }""")]
    [InlineData("""{ "eventId": "not-a-guid", "eventType": "TripStartReported", "correlationId": "c", "tripNumber": "TR-0000012", "driverId": "DRV-0002" }""")]
    public async Task AMessageThatIsNotAReadableTripStartReported_IsDeadLetteredAsUnreadable_AndNothingIsCalled(string message)
    {
        var outcome = await _world.RunAsync(message);

        outcome.Should().Be(DriverReportsRunOutcome.Unreadable);
        _world.Settled.Should().ContainSingle().Which.Settlement.Should().BeOfType<Settlement.DeadLetter>().Which.Reason.Should().Be("DriverReportUnreadable");
        _world.Tms.Requests.Should().BeEmpty();
        _world.DriverApp.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task WhenTheDriverAppCannotBeReached_TheOutcomeIsRetried_ThenTheRunFails_WithTheMessageAlreadySettled()
    {
        _world.DriverApp.Otherwise(HttpStatusCode.ServiceUnavailable);

        var outcome = await _world.RunAsync(DriverReportsWorld.StartReported());

        outcome.Should().Be(DriverReportsRunOutcome.OutcomeNotReported);
        _world.DriverApp.Requests.Should().HaveCount(4);
        _world.Settled.Should().ContainSingle().Which.Settlement.Should().Be(Settlement.Complete);
    }

    [Fact]
    public async Task AnOutcomeTheDriverAppRefuses_IsNotRetried()
    {
        _world.DriverApp.Otherwise(HttpStatusCode.NotFound);

        var outcome = await _world.RunAsync(DriverReportsWorld.StartReported());

        outcome.Should().Be(DriverReportsRunOutcome.OutcomeNotReported);
        _world.DriverApp.Requests.Should().ContainSingle();
    }

    [Fact]
    public async Task ARunWithNoRetries_GivesUpAtTheFirstServerError()
    {
        var world = new DriverReportsWorld(retryCount: 0);
        world.Tms.Otherwise(HttpStatusCode.InternalServerError);

        await world.RunAsync(DriverReportsWorld.StartReported());

        world.Tms.Requests.Should().ContainSingle();
        world.OutcomeReported()["message"]!.GetValue<string>().Should().Contain("1 attempt").And.NotContain("1 attempts");
    }

    [Fact]
    public async Task ALongTmsMessage_IsShortenedToWhatTheDriverAppTakes()
    {
        _world.Tms.Otherwise(HttpStatusCode.Conflict, DriverReportsWorld.Problem(409, "NotTripDay", new string('x', 3000)));

        await _world.RunAsync(DriverReportsWorld.StartReported());

        _world.OutcomeReported()["message"]!.GetValue<string>().Length.Should().Be(1000);
    }

    [Fact]
    public async Task TheOutcomeReports_ValidateAgainstTheCanonicalModel()
    {
        var schema = JsonSchema.FromText(ReadResource("CanonicalModel/driver-report-outcome.v1.schema.json"));
        var recorded = new DriverReportsWorld();
        var refused = new DriverReportsWorld();
        var lost = new DriverReportsWorld();
        refused.Tms.Otherwise(HttpStatusCode.Conflict, DriverReportsWorld.Problem(409, "NotTripDay", "Wrong day."));
        lost.Tms.Otherwise(HttpStatusCode.ServiceUnavailable);

        foreach (var world in new[] { recorded, refused, lost })
        {
            await world.RunAsync(DriverReportsWorld.StartReported());
            var results = schema.Evaluate(JsonSerializer.SerializeToElement(world.OutcomeReported()), new EvaluationOptions { OutputFormat = OutputFormat.List, RequireFormatValidation = true });
            results.IsValid.Should().BeTrue(world.DriverApp.Requests[0].Body);
        }
    }

    [Fact]
    public void TheEventTheDriverAppPublishes_IsTheOneTheFlowReads()
    {
        var schema = JsonSchema.FromText(ReadResource("CanonicalModel/trip-start-reported.v1.schema.json"));

        var results = schema.Evaluate(JsonSerializer.SerializeToElement(JsonNode.Parse(DriverReportsWorld.StartReported())), new EvaluationOptions { OutputFormat = OutputFormat.List, RequireFormatValidation = true });

        results.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task ARecordedStopReport_IsPostedToTheTrackingEventApi_AsOwnFleet_WithTheDriverIdFromTheEvent_AndTheRecordRole()
    {
        var outcome = await _world.RunAsync(DriverReportsWorld.StopReported("CollectionFailed", remark: "Nobody at the dock"));

        outcome.Should().Be(DriverReportsRunOutcome.Done);
        var call = _world.Tms.Requests.Should().ContainSingle().Subject;
        call.Uri.ToString().Should().Be("http://tms.test/v1/tracking-events");
        call.Headers["X-Correlation-ID"].Should().Be(DriverReportsWorld.StopCorrelationId);
        DriverReportsWorld.RoleOf(call).Should().Be("TrackingEvents.Record");
        var body = JsonNode.Parse(call.Body)!;
        body["eventId"]!.GetValue<string>().Should().Be(DriverReportsWorld.StopEventId, "the TMS recognises a repeat by the Driver app event ID");
        body["source"]!["type"]!.GetValue<string>().Should().Be("OwnFleet");
        body["source"]!["driverId"]!.GetValue<string>().Should().Be("DRV-0002", "the Driver ID is the one the Driver app took from the Driver's token");
        body["tripNumber"]!.GetValue<string>().Should().Be("TR-0000012");
        body["shipmentNumber"]!.GetValue<string>().Should().Be("SH-0000031");
        body["tripVersion"]!.GetValue<int>().Should().Be(3);
        body["type"]!.GetValue<string>().Should().Be("CollectionFailed");
        body["eventTime"]!.GetValue<string>().Should().Be("2030-10-03T08:41:03+02:00");
        body["location"]!["place"]!.GetValue<string>().Should().Be("Rotterdam");
        body["location"]!["postcode"]!.GetValue<string>().Should().Be("3011 AA");
        body["location"]!["countryCode"]!.GetValue<string>().Should().Be("NL");
        body["remark"]!.GetValue<string>().Should().Be("Nobody at the dock");
        _world.Settled.Should().ContainSingle().Which.Settlement.Should().Be(Settlement.Complete);
        var reported = _world.OutcomeReported();
        reported["outcome"]!.GetValue<string>().Should().Be("Recorded");
        reported["driverAppEventId"]!.GetValue<string>().Should().Be(DriverReportsWorld.StopEventId);
        reported["correlationId"]!.GetValue<string>().Should().Be(DriverReportsWorld.StopCorrelationId);
    }

    [Theory]
    [InlineData("Collected", "Collection", null)]
    [InlineData("DeliveryFailed", "Delivery", "Dock closed")]
    [InlineData("Damaged", "Delivery", "Crushed carton")]
    [InlineData("Delivered", "Delivery", null)]
    public async Task TheTrackingEventTheFlowPosts_ValidatesAgainstTheCanonicalModel(string type, string stop, string? remark)
    {
        var schema = TrackingEventSchema.Value;

        await _world.RunAsync(DriverReportsWorld.StopReported(type, stop, remark));

        var body = JsonNode.Parse(_world.Tms.Requests.Single().Body)!;
        schema.Evaluate(JsonSerializer.SerializeToElement(body), new EvaluationOptions { OutputFormat = OutputFormat.List, RequireFormatValidation = true }).IsValid.Should().BeTrue(body.ToJsonString());
        (body as JsonObject)!.ContainsKey("remark").Should().Be(remark is not null, "a remark the Driver didn't give is left out");
    }

    [Fact]
    public async Task AStopReportTheTmsRefuses_IsNotRetried_DeadLettersTheMessageWithTheTmsCode_AndIsReportedRefused()
    {
        _world.Tms.Otherwise(HttpStatusCode.Conflict, DriverReportsWorld.Problem(409, "CollectionBeforeDelivery", "SH-0000031's collection is not done."));

        var outcome = await _world.RunAsync(DriverReportsWorld.StopReported("DeliveryFailed", "Delivery", "Closed"));

        outcome.Should().Be(DriverReportsRunOutcome.Done);
        _world.Tms.Requests.Should().ContainSingle();
        _world.Settled.Should().ContainSingle().Which.Settlement.Should().Be(new Settlement.DeadLetter("CollectionBeforeDelivery", "SH-0000031's collection is not done."));
        var reported = _world.OutcomeReported();
        reported["outcome"]!.GetValue<string>().Should().Be("Refused");
        reported["failureCode"]!.GetValue<string>().Should().Be("CollectionBeforeDelivery");
    }

    [Fact]
    public async Task AStopReportWhileTheTmsIsUnavailable_IsRetried_ThenDeadLettered_AndReportedNotRecorded()
    {
        _world.Tms.Otherwise(HttpStatusCode.ServiceUnavailable, "Service Unavailable");

        await _world.RunAsync(DriverReportsWorld.StopReported());

        _world.Tms.Requests.Should().HaveCount(4);
        _world.Tms.Requests.Should().OnlyContain(request => request.Uri.AbsolutePath == "/v1/tracking-events");
        _world.Settled.Single().Settlement.Should().BeOfType<Settlement.DeadLetter>().Which.Reason.Should().Be("TmsUnavailable");
        _world.OutcomeReported()["outcome"]!.GetValue<string>().Should().Be("NotRecorded");
    }

    [Fact]
    public async Task AStopReportTheTmsRecognisesAsARepeat_IsRecordedAsAny()
    {
        _world.Tms.Otherwise(HttpStatusCode.OK, """{ "repeat": true }""");

        await _world.RunAsync(DriverReportsWorld.StopReported());

        _world.Settled.Single().Settlement.Should().Be(Settlement.Complete);
        _world.OutcomeReported()["outcome"]!.GetValue<string>().Should().Be("Recorded");
    }

    [Theory]
    [InlineData("""{ "eventId": "0198e7c0-0000-7000-8000-0000000000d1", "eventType": "StopCompletedReported", "correlationId": "c", "tripNumber": "TR-0000012", "driverId": "DRV-0002", "version": 3, "stop": "Collection", "trackingEventType": "Collected", "location": { "place": "Rotterdam", "postcode": "3011 AA", "countryCode": "NL" } }""")]
    [InlineData("""{ "eventId": "0198e7c0-0000-7000-8000-0000000000d1", "eventType": "StopCompletedReported", "correlationId": "c", "tripNumber": "TR-0000012", "driverId": "DRV-0002", "version": 3, "shipmentNumber": "SH-0000031", "stop": "Collection", "location": { "place": "Rotterdam", "postcode": "3011 AA", "countryCode": "NL" } }""")]
    [InlineData("""{ "eventId": "0198e7c0-0000-7000-8000-0000000000d1", "eventType": "StopCompletedReported", "correlationId": "c", "tripNumber": "TR-0000012", "driverId": "DRV-0002", "version": 3, "shipmentNumber": "SH-0000031", "stop": "Collection", "trackingEventType": "Collected" }""")]
    [InlineData("""{ "eventId": "0198e7c0-0000-7000-8000-0000000000d1", "eventType": "StopCompletedReported", "correlationId": "c", "tripNumber": "TR-0000012", "driverId": "DRV-0002", "shipmentNumber": "SH-0000031", "stop": "Collection", "trackingEventType": "Collected", "location": { "place": "Rotterdam", "postcode": "3011 AA", "countryCode": "NL" } }""")]
    [InlineData("""{ "eventId": "0198e7c0-0000-7000-8000-0000000000d1", "eventType": "StopCompletedReported", "correlationId": "c", "tripNumber": "TR-0000012", "driverId": "DRV-0002", "version": 3, "shipmentNumber": "SH-0000031", "stop": "Collection", "trackingEventType": "Collected", "location": { "place": "Rotterdam", "postcode": "3011 AA" } }""")]
    public async Task AStopReportMissingWhatTheTmsNeeds_IsDeadLetteredAsUnreadable_AndNothingIsCalled(string message)
    {
        var outcome = await _world.RunAsync(message);

        outcome.Should().Be(DriverReportsRunOutcome.Unreadable);
        _world.Settled.Should().ContainSingle().Which.Settlement.Should().BeOfType<Settlement.DeadLetter>().Which.Reason.Should().Be("DriverReportUnreadable");
        _world.Tms.Requests.Should().BeEmpty();
        _world.DriverApp.Requests.Should().BeEmpty();
    }

    [Fact]
    public void TheStopEventsTheDriverAppPublishes_AreTheOnesTheFlowReads()
    {
        var schema = StopReportedSchema.Value;
        var options = new EvaluationOptions { OutputFormat = OutputFormat.List, RequireFormatValidation = true };

        schema.Evaluate(JsonSerializer.SerializeToElement(JsonNode.Parse(DriverReportsWorld.StopReported())), options).IsValid.Should().BeTrue();
        schema.Evaluate(JsonSerializer.SerializeToElement(JsonNode.Parse(DriverReportsWorld.StopReported("DeliveryFailed", "Delivery", "Closed"))), options).IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task EveryStopSampleOfTheContract_IsRead_AndBecomesAValidTrackingEventOfOwnFleet()
    {
        var samples = typeof(DriverReportsFlowTests).Assembly.GetManifestResourceNames().Where(name => name.StartsWith("CanonicalModel/samples/stop-completed-reported/", StringComparison.Ordinal)).ToList();
        samples.Should().HaveCountGreaterThanOrEqualTo(3);

        foreach (var sample in samples)
        {
            var world = new DriverReportsWorld();
            var sent = ReadResource(sample);

            var outcome = await world.RunAsync(sent);

            outcome.Should().Be(DriverReportsRunOutcome.Done, sample);
            var body = JsonNode.Parse(world.Tms.Requests.Single().Body)!;
            TrackingEventSchema.Value.Evaluate(JsonSerializer.SerializeToElement(body), new EvaluationOptions { OutputFormat = OutputFormat.List, RequireFormatValidation = true })
                .IsValid.Should().BeTrue($"{sample}: {body.ToJsonString()}");
            body["type"]!.GetValue<string>().Should().Be(JsonNode.Parse(sent)!["trackingEventType"]!.GetValue<string>());
            body["source"]!["driverId"]!.GetValue<string>().Should().Be(JsonNode.Parse(sent)!["driverId"]!.GetValue<string>());
        }
    }

    private static string ReadResource(string name)
    {
        using var stream = typeof(DriverReportsFlowTests).Assembly.GetManifestResourceStream(name) ?? throw new InvalidOperationException($"{name} is not embedded.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
