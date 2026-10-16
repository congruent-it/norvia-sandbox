using System.Diagnostics;
using System.Net;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging.Testing;
using Norvia.Simulator.Domain.Log;
using Norvia.Simulator.Domain.Planner;
using Norvia.Simulator.Infrastructure.PartnerApi;
using Norvia.Simulator.Infrastructure.TmsApi;
using Norvia.Simulator.Infrastructure.UnitTests.PartnerApi;

namespace Norvia.Simulator.Infrastructure.UnitTests.TmsApi;

public sealed class NorviaTmsApiClientTests : IDisposable
{
    private static readonly Assignee VanDijk = new("CAR-0001", "Van Dijk Transport");

    private readonly StubHandler _tms = new();
    private readonly ITmsTokens _tokens = Substitute.For<ITmsTokens>();
    private readonly FakeLogger<NorviaTmsApiClient> _logger = new();
    private readonly SimulatorActivities _activities = new("Norvia.Simulator.Web.Tests.Tms");
    private readonly List<Activity> _spans = [];
    private readonly ActivityListener _listener;
    private readonly HttpClient _http;
    private readonly NorviaTmsApiClient _client;

    public NorviaTmsApiClientTests()
    {
        _http = new HttpClient(_tms) { BaseAddress = new Uri("https://tms.example/") };
        _tokens.GetTokenAsync(Arg.Any<CancellationToken>()).Returns("planner-token");
        _client = new NorviaTmsApiClient(_http, _tokens, _activities, _logger);
        _listener = new ActivityListener
        {
            ShouldListenTo = source => source == _activities.Source,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = _spans.Add,
        };
        ActivitySource.AddActivityListener(_listener);
    }

    public void Dispose()
    {
        _listener.Dispose();
        _activities.Dispose();
        _http.Dispose();
        _tms.Dispose();
    }

    [Fact]
    public async Task Assign_ToACarrier_PostsTheAssignmentAsThePlanner_UnderItsKey()
    {
        _tms.Answer(HttpStatusCode.Created, """{"shipmentNumber":"SH-1","correlationId":"plan-1","shipperId":"SHP-0001"}""");

        await _client.AssignAsync("TO-0000042", VanDijk, "SIM-3-7-assign", CancellationToken.None);

        _tms.Request!.Method.Should().Be(HttpMethod.Post);
        _tms.Request.RequestUri.Should().Be(new Uri("https://tms.example/v1/transport-orders/TO-0000042/assign"));
        _tms.Request.Headers.Authorization!.Parameter.Should().Be("planner-token");
        _tms.Request.Headers.GetValues("Idempotency-Key").Should().Equal("SIM-3-7-assign");
        var sent = JsonNode.Parse(_tms.RequestBody!)!;
        sent["assignedTo"]!.GetValue<string>().Should().Be("Carrier");
        sent["carrierId"]!.GetValue<string>().Should().Be("CAR-0001");
    }

    [Fact]
    public async Task Assign_ToOwnFleet_NamesNoCarrier()
    {
        _tms.Answer(HttpStatusCode.Created, """{"shipmentNumber":"SH-1","correlationId":"plan-1"}""");

        await _client.AssignAsync("TO-1", Assignee.OwnFleet, "key", CancellationToken.None);

        var sent = JsonNode.Parse(_tms.RequestBody!)!.AsObject();
        sent["assignedTo"]!.GetValue<string>().Should().Be("OwnFleet");
        sent.ContainsKey("carrierId").Should().BeFalse("the TMS refuses an Own fleet assignment that names a Carrier");
    }

    [Fact]
    public async Task Assign_APlannedShipment_GivesItsNumberAndTrail()
    {
        _tms.Answer(HttpStatusCode.Created, """{"shipmentNumber":"SH-0000009","correlationId":"plan-9"}""");

        var answer = await _client.AssignAsync("TO-9", VanDijk, "key", CancellationToken.None);

        answer.Should().Be(new Application.Ports.PlannerAnswer(LogOutcome.Planned, "plan-9", "SH-0000009", null));
    }

    [Fact]
    public async Task Assign_ARefusal_IsAnOutcomeNotAnError()
    {
        _tms.Answer(HttpStatusCode.UnprocessableEntity, """{"code":"InactiveCarrier","detail":"x","correlationId":"refused-3"}""");

        var answer = await _client.AssignAsync("TO-3", VanDijk, "key", CancellationToken.None);

        answer.Outcome.Should().Be(LogOutcome.PlanningRefused);
        answer.Detail.Should().Be("InactiveCarrier (422)");
        answer.CorrelationId.Should().Be("refused-3");
    }

    [Fact]
    public async Task Assign_TheTmsUnreachable_IsNotReceived()
    {
        _tms.Fail(new HttpRequestException("down"));

        var answer = await _client.AssignAsync("TO-3", VanDijk, "key", CancellationToken.None);

        answer.Outcome.Should().Be(LogOutcome.PlanningNotReceived);
        _logger.Collector.GetSnapshot().Should().Contain(record => record.Level == Microsoft.Extensions.Logging.LogLevel.Warning);
    }

    [Fact]
    public async Task Assign_NoToken_IsNotReceived()
    {
        _tokens.GetTokenAsync(Arg.Any<CancellationToken>()).Returns<string>(_ => throw new InvalidOperationException("TmsApi:LocalTokens isn't configured."));

        var answer = await _client.AssignAsync("TO-3", VanDijk, "key", CancellationToken.None);

        answer.Outcome.Should().Be(LogOutcome.PlanningNotReceived);
        _tms.Request.Should().BeNull();
    }

    private static Domain.Trips.TripDraft Draft() => new(
        new DateOnly(2026, 10, 19),
        new Domain.Worlds.SimulatedVehicle("VEH-0001", "VN-101-A", Domain.Worlds.VehicleKind.Van, 1200, 4),
        new Domain.Worlds.SimulatedDriver("DRV-0006", "Sanne Jansen", [Domain.Worlds.LicenceCategory.B]),
        new TimeOnly(7, 30), TimeSpan.FromMinutes(165),
        [new Domain.Trips.TripShipment("SH-0000001", new FleetLoad(400, 1), new DateTime(2026, 10, 19, 8, 0, 0)), new Domain.Trips.TripShipment("SH-0000002", new FleetLoad(300, 1), new DateTime(2026, 10, 19, 9, 0, 0))]);

    [Fact]
    public async Task CreateTrip_PostsTheDraftAsThePlanner_WithItsVehicleDriverStartAndShipments_UnderItsKey()
    {
        _tms.Answer(HttpStatusCode.Created, """{"number":"TR-0000012","warnings":[]}""");

        var answer = await _client.CreateTripAsync(Draft(), "SIM-3-20745-1-trip", CancellationToken.None);

        _tms.Request!.Method.Should().Be(HttpMethod.Post);
        _tms.Request.RequestUri.Should().Be(new Uri("https://tms.example/v1/trips"));
        _tms.Request.Headers.Authorization!.Parameter.Should().Be("planner-token");
        _tms.Request.Headers.GetValues("Idempotency-Key").Should().Equal("SIM-3-20745-1-trip");
        var sent = JsonNode.Parse(_tms.RequestBody!)!;
        sent["day"]!.GetValue<string>().Should().Be("2026-10-19");
        sent["vehicleId"]!.GetValue<string>().Should().Be("VEH-0001");
        sent["driverId"]!.GetValue<string>().Should().Be("DRV-0006");
        sent["plannedStart"]!.GetValue<string>().Should().Be("07:30:00");
        sent["plannedDrivingDuration"]!.GetValue<string>().Should().Be("02:45:00");
        sent["shipmentNumbers"]!.AsArray().Select(node => node!.GetValue<string>()).Should().Equal("SH-0000001", "SH-0000002");
        answer.Should().Be(new Application.Ports.PlannerAnswer(LogOutcome.TripBuilt, null, null, null, TripNumber: "TR-0000012"));
    }

    [Fact]
    public async Task CreateTrip_IsSentInASpanNamedLikeTheTmsUis_AndLoggedWithTheTripAndTheTripFlow()
    {
        _tms.Answer(HttpStatusCode.Created, """{"number":"TR-0000012"}""");

        await _client.CreateTripAsync(Draft(), "key", CancellationToken.None);

        _spans.Should().ContainSingle().Which.OperationName.Should().Be("Create Trip");
        var scope = _logger.Collector.GetSnapshot().Single(record => record.Message.Contains("answered")).Scopes.Single() as IEnumerable<KeyValuePair<string, object?>>;
        scope!.ToDictionary().Should().BeEquivalentTo(new Dictionary<string, object?> { ["flow"] = "trip-dispatch", ["outcome"] = "Succeeded", ["tripNumber"] = "TR-0000012" });
    }

    [Fact]
    public async Task CreateTrip_ARefusal_IsAnOutcomeNotAnError()
    {
        _tms.Answer(HttpStatusCode.Conflict, """{"code":"OutsideShift","detail":"x"}""");

        var answer = await _client.CreateTripAsync(Draft(), "key", CancellationToken.None);

        answer.Outcome.Should().Be(LogOutcome.PlanningRefused);
        answer.Detail.Should().Be("OutsideShift (409)");
    }

    [Fact]
    public async Task DispatchTrip_PostsToTheTripsDispatch_AndGivesTheTrailOfTheDispatch()
    {
        _tms.Answer(HttpStatusCode.OK, """{"number":"TR-0000012","version":1,"correlationId":"disp-1"}""");

        var answer = await _client.DispatchTripAsync("TR-0000012", CancellationToken.None);

        _tms.Request!.Method.Should().Be(HttpMethod.Post);
        _tms.Request.RequestUri.Should().Be(new Uri("https://tms.example/v1/trips/TR-0000012/dispatch"));
        _tms.Request.Headers.Contains("Idempotency-Key").Should().BeFalse("a Dispatch is not retried by the TMS UI either");
        answer.Should().Be(new Application.Ports.PlannerAnswer(LogOutcome.TripDispatched, "disp-1", null, null, TripNumber: "TR-0000012"));
        _spans.Should().ContainSingle().Which.OperationName.Should().Be("Dispatch Trip");
    }

    [Fact]
    public async Task DispatchTrip_TheTmsUnreachable_IsNotReceived()
    {
        _tms.Fail(new HttpRequestException("down"));

        var answer = await _client.DispatchTripAsync("TR-0000012", CancellationToken.None);

        answer.Outcome.Should().Be(LogOutcome.PlanningNotReceived);
    }

    [Fact]
    public async Task Assign_IsSentInASpanNamedLikeTheTmsUis()
    {
        _tms.Answer(HttpStatusCode.Created, """{"shipmentNumber":"SH-1","correlationId":"c"}""");

        await _client.AssignAsync("TO-1", VanDijk, "key", CancellationToken.None);

        _spans.Should().ContainSingle().Which.OperationName.Should().Be("Assign Transport Order");
    }

    [Fact]
    public async Task Assign_LogsTheAnswerWithTheKeysTheSimulatorKnows()
    {
        _tms.Answer(HttpStatusCode.Created, """{"shipmentNumber":"SH-1","correlationId":"plan-1"}""");

        await _client.AssignAsync("TO-1", VanDijk, "key", CancellationToken.None);

        var scope = _logger.Collector.GetSnapshot().Single(record => record.Message.Contains("answered")).Scopes.Single() as IEnumerable<KeyValuePair<string, object?>>;
        scope!.ToDictionary().Should().BeEquivalentTo(new Dictionary<string, object?>
        {
            ["flow"] = "transport-planning",
            ["outcome"] = "Succeeded",
            ["correlationId"] = "plan-1",
            ["transportOrderNumber"] = "TO-1",
            ["shipmentNumber"] = "SH-1",
        });
    }

    [Fact]
    public void TrackingKeysOf_ARefusal_NamesTheFailureCode_AndLeavesOutWhatItDoesNotKnow()
    {
        var keys = NorviaTmsApiClient.TrackingKeysOf(
            new Application.Ports.PlannerAnswer(LogOutcome.PlanningRefused, null, null, "TransportOrderAlreadyPlanned (409)", "TransportOrderAlreadyPlanned"), "TO-1", shipmentNumber: null);

        keys.Should().BeEquivalentTo(new Dictionary<string, object?>
        {
            ["flow"] = "transport-planning",
            ["outcome"] = "Refused",
            ["failureCode"] = "TransportOrderAlreadyPlanned",
            ["transportOrderNumber"] = "TO-1",
        });
    }

    [Fact]
    public void TrackingKeysOf_NotReceived_IsFailed()
    {
        var keys = NorviaTmsApiClient.TrackingKeysOf(PlannerAnswers.NotReceived("x"), "TO-1", null);

        keys["outcome"].Should().Be("Failed");
        keys["failureCode"].Should().Be("PlanningNotReceived");
    }

    [Fact]
    public async Task Reassign_PostsToTheShipment_AndKeepsItsNumber()
    {
        _tms.Answer(HttpStatusCode.OK, """{"carriedBy":{},"transportOrderNumber":"TO-1","correlationId":"re-1"}""");

        var answer = await _client.ReassignAsync("SH-0000007", Assignee.OwnFleet, "SIM-3-7-reassign", CancellationToken.None);

        _tms.Request!.RequestUri.Should().Be(new Uri("https://tms.example/v1/shipments/SH-0000007/reassign"));
        _tms.Request.Headers.GetValues("Idempotency-Key").Should().Equal("SIM-3-7-reassign");
        answer.Should().Be(new Application.Ports.PlannerAnswer(LogOutcome.Reassigned, "re-1", "SH-0000007", null));
        _spans.Should().ContainSingle().Which.OperationName.Should().Be("Reassign Shipment");
    }

    [Fact]
    public async Task GetOwnFleetDays_ReadsTheOpenDaysAsThePlanner()
    {
        _tms.Answer(HttpStatusCode.OK, """[{"date":"2026-10-14","availableVehicles":1,"shifts":[],"load":{"capacityKg":5000,"capacityPalletPlaces":12,"demandKg":0,"demandPalletPlaces":0,"over":false}}]""");

        var days = await _client.GetOwnFleetDaysAsync(CancellationToken.None);

        _tms.Request!.Method.Should().Be(HttpMethod.Get);
        _tms.Request.RequestUri.Should().Be(new Uri("https://tms.example/v1/capacity/days"));
        _tms.Request.Headers.Authorization!.Parameter.Should().Be("planner-token");
        days.Should().ContainSingle().Which.Should().BeEquivalentTo(new FleetDay(new DateOnly(2026, 10, 14), 5000, 12, 0, 0));
    }

    [Fact]
    public async Task GetOwnFleetDays_TheTmsRefuses_IsUnknown()
    {
        _tms.Answer(HttpStatusCode.Forbidden, "{}");

        (await _client.GetOwnFleetDaysAsync(CancellationToken.None)).Should().BeNull();
    }

    [Fact]
    public async Task GetOwnFleetDays_TheTmsUnreachable_IsUnknown()
    {
        _tms.Fail(new HttpRequestException("down"));

        (await _client.GetOwnFleetDaysAsync(CancellationToken.None)).Should().BeNull();
    }

    [Fact]
    public async Task Release_PostsToTheShipmentAsThePlanner_UnderItsKeyAndWithNoBody()
    {
        _tms.Answer(HttpStatusCode.OK, """{"shipmentNumber":"SH-1","carrierBookingNumber":"CB-0000042","correlationId":"rel-1"}""");

        var answer = await _client.ReleaseAsync("SH-0000007", "SIM-3-7-release", CancellationToken.None);

        _tms.Request!.Method.Should().Be(HttpMethod.Post);
        _tms.Request.RequestUri.Should().Be(new Uri("https://tms.example/v1/shipments/SH-0000007/release"));
        _tms.Request.Headers.Authorization!.Parameter.Should().Be("planner-token");
        _tms.Request.Headers.GetValues("Idempotency-Key").Should().Equal("SIM-3-7-release");
        answer.Outcome.Should().Be(LogOutcome.Released);
        answer.CarrierBookingNumber.Should().Be("CB-0000042");
        answer.CorrelationId.Should().Be("rel-1");
    }

    [Fact]
    public async Task Release_IsSentInASpanNamedLikeTheTmsUis_AndLoggedWithTheKeysTheSimulatorKnows()
    {
        _tms.Answer(HttpStatusCode.OK, """{"shipmentNumber":"SH-0000007","carrierBookingNumber":"CB-0000042","correlationId":"rel-1"}""");

        await _client.ReleaseAsync("SH-0000007", "key", CancellationToken.None);

        _spans.Should().ContainSingle().Which.OperationName.Should().Be("Release Shipment");
        var keys = _logger.LatestRecord.Scopes.OfType<IEnumerable<KeyValuePair<string, object?>>>().SelectMany(scope => scope).ToDictionary(pair => pair.Key, pair => pair.Value);
        keys["flow"].Should().Be("transport-planning");
        keys["outcome"].Should().Be("Succeeded");
        keys["correlationId"].Should().Be("rel-1");
        keys["shipmentNumber"].Should().Be("SH-0000007");
    }

    [Fact]
    public async Task Release_TheTmsUnreachable_IsNotReceived()
    {
        _tms.Fail(new HttpRequestException("down"));

        (await _client.ReleaseAsync("SH-1", "key", CancellationToken.None)).Outcome.Should().Be(LogOutcome.PlanningNotReceived);
    }

    [Fact]
    public async Task GetOpenExceptions_ReadsTheListAsThePlanner()
    {
        _tms.Answer(HttpStatusCode.OK, """[{"exceptionNumber":"EXC-1","shipmentNumber":"SH-1","type":"Damaged"}]""");

        var open = await _client.GetOpenExceptionsAsync(CancellationToken.None);

        _tms.Request!.RequestUri.Should().Be(new Uri("https://tms.example/v1/exceptions/open"));
        _tms.Request.Headers.Authorization!.Parameter.Should().Be("planner-token");
        open.Should().ContainSingle().Which.ExceptionNumber.Should().Be("EXC-1");
    }

    [Fact]
    public async Task GetOpenExceptions_TheTmsRefusesOrIsUnreachable_IsUnknown()
    {
        _tms.Answer(HttpStatusCode.InternalServerError, "{}");
        (await _client.GetOpenExceptionsAsync(CancellationToken.None)).Should().BeNull();

        _tms.Fail(new HttpRequestException("down"));
        (await _client.GetOpenExceptionsAsync(CancellationToken.None)).Should().BeNull();
    }

    [Fact]
    public async Task CancelCarrierBooking_PostsTheReasonUnderItsKey()
    {
        _tms.Answer(HttpStatusCode.OK, """{"exceptionNumber":"EXC-1","shipmentNumber":"SH-1","carrierBookingNumber":"CB-1","correlationId":"can-1"}""");

        var answer = await _client.CancelCarrierBookingAsync("EXC-0000001", "Won't collect", "SIM-3-7-cancel", CancellationToken.None);

        _tms.Request!.RequestUri.Should().Be(new Uri("https://tms.example/v1/exceptions/EXC-0000001/cancel-carrier-booking"));
        _tms.Request.Headers.GetValues("Idempotency-Key").Should().Equal("SIM-3-7-cancel");
        JsonNode.Parse(_tms.RequestBody!)!["reason"]!.GetValue<string>().Should().Be("Won't collect");
        answer.Outcome.Should().Be(LogOutcome.BookingCancelled);
        _spans.Should().ContainSingle().Which.OperationName.Should().Be("Cancel Carrier booking");
    }

    [Fact]
    public async Task ResolveReplanned_PostsTheOutcomeAndTheRemark()
    {
        _tms.Answer(HttpStatusCode.OK, """{"exceptionNumber":"EXC-1","shipmentNumber":"SH-1","status":"Resolved","outcome":"Replanned","correlationId":"res-1"}""");

        var answer = await _client.ResolveReplannedAsync("EXC-0000001", "Replanned", CancellationToken.None);

        _tms.Request!.RequestUri.Should().Be(new Uri("https://tms.example/v1/exceptions/EXC-0000001/resolve"));
        var sent = JsonNode.Parse(_tms.RequestBody!)!;
        sent["outcome"]!.GetValue<string>().Should().Be("Replanned");
        sent["remark"]!.GetValue<string>().Should().Be("Replanned");
        answer.Outcome.Should().Be(LogOutcome.ExceptionResolved);
        _spans.Should().ContainSingle().Which.OperationName.Should().Be("Resolve Exception");
    }

    [Fact]
    public void TrackingKeysOf_ExceptionHandling_IsItsOwnFlow()
    {
        var keys = NorviaTmsApiClient.TrackingKeysOf(new Application.Ports.PlannerAnswer(LogOutcome.BookingCancelled, "c", "SH-1", null), null, null, "exception-handling");

        keys["flow"].Should().Be("exception-handling");
        keys["outcome"].Should().Be("Succeeded");
    }
}
