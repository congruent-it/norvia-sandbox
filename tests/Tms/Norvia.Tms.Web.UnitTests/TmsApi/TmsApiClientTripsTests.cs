using System.Net;
using Microsoft.Extensions.Logging.Testing;
using Norvia.Tms.Domain.Trips;
using Norvia.Tms.Web.TmsApi;
using Norvia.Tms.Web.Trips;

namespace Norvia.Tms.Web.UnitTests.TmsApi;

/// <summary>The Planner's actions on Draft Trips, as the TMS UI sends them to the TMS API (P11).</summary>
public sealed class TmsApiClientTripsTests : IDisposable
{
    private static readonly CancellationToken Cancel = Xunit.TestContext.Current.CancellationToken;

    private readonly StubHandler _tmsApi = new();
    private readonly ITmsApiTokens _tokens = Substitute.For<ITmsApiTokens>();
    private readonly FakeLogger<TmsApiClient> _logger = new();
    private readonly TmsUiActivities _activities = new("Norvia.Tms.Web.Tests");
    private readonly HttpClient _http;
    private readonly TmsApiClient _client;

    public TmsApiClientTripsTests()
    {
        _http = new HttpClient(_tmsApi) { BaseAddress = new Uri("http://tms-api.example") };
        _tokens.GetTokenAsync(Arg.Any<CancellationToken>()).Returns("tms-ui-token");
        _client = new TmsApiClient(_http, _tokens, _activities, _logger);
    }

    public void Dispose()
    {
        _activities.Dispose();
        _http.Dispose();
        _tmsApi.Dispose();
    }

    [Fact]
    public async Task CreateDraftTripAsync_PostsTheDayVehicleAndDriver_AndAnswersTheNumber()
    {
        _tmsApi.Answer(HttpStatusCode.Created, """{ "number": "TR-0000001" }""");

        var answer = await _client.CreateDraftTripAsync(new TripEntry(new DateOnly(2026, 10, 14), "VEH-0001", null), Cancel);

        answer.Should().Be(new TripAnswer.Created("TR-0000001"));
        _tmsApi.Request!.Method.Should().Be(HttpMethod.Post);
        _tmsApi.Request.RequestUri.Should().Be(new Uri("http://tms-api.example/v1/trips"));
        _tmsApi.Request.Headers.Authorization!.Parameter.Should().Be("tms-ui-token");
        _tmsApi.RequestBody.Should().Be("""{"day":"2026-10-14","vehicleId":"VEH-0001"}""");
        _tmsApi.Request.Headers.GetValues("Idempotency-Key").Single().Should().NotBeNullOrWhiteSpace("a retried create must not make another Draft");
    }

    [Fact]
    public async Task CreateDraftTripAsync_WithShipments_PostsTheirNumbers_AndAnswersTheWarnings()
    {
        _tmsApi.Answer(HttpStatusCode.Created, """{ "number": "TR-0000001", "warnings": [{ "code": "DeliveryWindowOutsideTripDay", "message": "Shipment SH-0000012's delivery window doesn't include the Trip's day." }] }""");

        var answer = await _client.CreateDraftTripAsync(new TripEntry(new DateOnly(2026, 10, 14), null, null, ShipmentNumbers: ["SH-0000012", "SH-0000013"]), Cancel);

        answer.Should().BeOfType<TripAnswer.Created>().Which.Warnings.Should().ContainSingle().Which.Code.Should().Be("DeliveryWindowOutsideTripDay");
        _tmsApi.RequestBody.Should().Be("""{"day":"2026-10-14","shipmentNumbers":["SH-0000012","SH-0000013"]}""");
    }

    [Fact]
    public async Task AddShipmentsToTripAsync_PostsTheNumbersToTheTrip_AndAnswersTheWarnings()
    {
        _tmsApi.Answer(HttpStatusCode.OK, """{ "number": "TR-0000001", "warnings": [] }""");

        var answer = await _client.AddShipmentsToTripAsync("TR-0000001", ["SH-0000012"], Cancel);

        answer.Should().BeOfType<TripAnswer.Added>().Which.Warnings.Should().BeEmpty();
        _tmsApi.Request!.Method.Should().Be(HttpMethod.Post);
        _tmsApi.Request.RequestUri.Should().Be(new Uri("http://tms-api.example/v1/trips/TR-0000001/shipments"));
        _tmsApi.RequestBody.Should().Be("""{"shipmentNumbers":["SH-0000012"]}""");
        _tmsApi.Request.Headers.Contains("Idempotency-Key").Should().BeFalse("adding a Shipment that is on the Trip already changes nothing");
        ScopeOf(_logger.LatestRecord).Should().Contain(new Dictionary<string, object?>
        {
            ["flow"] = "trip-planning",
            ["outcome"] = "Succeeded",
            ["tripNumber"] = "TR-0000001",
        });
    }

    [Fact]
    public async Task AddShipmentsToTripAsync_ARefusal_AnswersItsReason_AndTracksItsCode()
    {
        _tmsApi.Answer(HttpStatusCode.Conflict, """{ "title": "The Shipments can't be put on the Trip.", "detail": "The Trip's 1,500 kg is more than Vehicle VEH-0001's 1,200 kg payload.", "code": "PayloadExceeded" }""");

        var answer = await _client.AddShipmentsToTripAsync("TR-0000001", ["SH-0000012"], Cancel);

        answer.Should().Be(new TripAnswer.Refused("The Trip's 1,500 kg is more than Vehicle VEH-0001's 1,200 kg payload."));
        ScopeOf(_logger.LatestRecord).Should().Contain(new Dictionary<string, object?>
        {
            ["flow"] = "trip-planning",
            ["outcome"] = "Refused",
            ["failureCode"] = "PayloadExceeded",
        });
    }

    [Fact]
    public async Task ReorderTripStopsAsync_PutsTheStopsInOrder_AndTracksTheTripNumber()
    {
        _tmsApi.Answer(HttpStatusCode.NoContent, string.Empty);

        var answer = await _client.ReorderTripStopsAsync("TR-0000001", [new("SH-0000002", StopKind.Collection), new("SH-0000002", StopKind.Delivery)], Cancel);

        answer.Should().Be(new TripAnswer.Done());
        _tmsApi.Request!.Method.Should().Be(HttpMethod.Put);
        _tmsApi.Request.RequestUri.Should().Be(new Uri("http://tms-api.example/v1/trips/TR-0000001/stops"));
        _tmsApi.RequestBody.Should().Be("""{"stops":[{"shipmentNumber":"SH-0000002","kind":"Collection"},{"shipmentNumber":"SH-0000002","kind":"Delivery"}]}""");
        ScopeOf(_logger.LatestRecord).Should().Contain(new Dictionary<string, object?>
        {
            ["flow"] = "trip-planning",
            ["outcome"] = "Succeeded",
            ["tripNumber"] = "TR-0000001",
        });
    }

    [Fact]
    public async Task ReorderTripStopsAsync_ADeliveryBeforeItsCollection_AnswersItsReason_AndTracksItsCode()
    {
        _tmsApi.Answer(HttpStatusCode.Conflict, """{ "title": "The Stops can't be put in that order.", "detail": "Shipment SH-0000002's delivery can't come before its collection.", "code": "CollectionBeforeDelivery" }""");

        var answer = await _client.ReorderTripStopsAsync("TR-0000001", [new("SH-0000002", StopKind.Delivery), new("SH-0000002", StopKind.Collection)], Cancel);

        answer.Should().Be(new TripAnswer.Refused("Shipment SH-0000002's delivery can't come before its collection."));
        ScopeOf(_logger.LatestRecord).Should().Contain(new Dictionary<string, object?>
        {
            ["flow"] = "trip-planning",
            ["outcome"] = "Refused",
            ["failureCode"] = "CollectionBeforeDelivery",
        });
    }

    [Fact]
    public async Task ChangeDraftTripAsync_PutsTheVehicleAndDriver_AndTracksTheTripNumber()
    {
        _tmsApi.Answer(HttpStatusCode.NoContent, string.Empty);

        var answer = await _client.ChangeDraftTripAsync("TR-0000001", new TripEntry(new DateOnly(2026, 10, 14), "VEH-0001", "DRV-0002"), Cancel);

        answer.Should().Be(new TripAnswer.Done());
        _tmsApi.Request!.Method.Should().Be(HttpMethod.Put);
        _tmsApi.Request.RequestUri.Should().Be(new Uri("http://tms-api.example/v1/trips/TR-0000001"));
        _tmsApi.RequestBody.Should().Be("""{"vehicleId":"VEH-0001","driverId":"DRV-0002"}""");
        ScopeOf(_logger.LatestRecord).Should().Contain(new Dictionary<string, object?>
        {
            ["flow"] = "trip-planning",
            ["outcome"] = "Succeeded",
            ["tripNumber"] = "TR-0000001",
        });
    }

    [Fact]
    public async Task ChangeDraftTripAsync_PutsThePlannedStartAndDrivingDuration()
    {
        _tmsApi.Answer(HttpStatusCode.NoContent, string.Empty);

        await _client.ChangeDraftTripAsync(
            "TR-0000001", new TripEntry(new DateOnly(2026, 10, 14), "VEH-0001", "DRV-0002", new TimeOnly(7, 30), TimeSpan.FromMinutes(390)), Cancel);

        _tmsApi.RequestBody.Should().Be("""{"vehicleId":"VEH-0001","driverId":"DRV-0002","plannedStart":"07:30:00","plannedDrivingDuration":"06:30:00"}""");
    }

    [Fact]
    public async Task DiscardDraftTripAsync_DeletesTheTrip()
    {
        _tmsApi.Answer(HttpStatusCode.NoContent, string.Empty);

        (await _client.DiscardDraftTripAsync("TR-0000001", Cancel)).Should().Be(new TripAnswer.Done());

        _tmsApi.Request!.Method.Should().Be(HttpMethod.Delete);
        _tmsApi.Request.RequestUri.Should().Be(new Uri("http://tms-api.example/v1/trips/TR-0000001"));
        _tmsApi.RequestBody.Should().BeNull();
    }

    [Theory]
    [InlineData(HttpStatusCode.Conflict)]
    [InlineData(HttpStatusCode.NotFound)]
    public async Task ARefusal_AnswersTheProblemsDetail(HttpStatusCode status)
    {
        _tmsApi.Answer(status, """{ "code": "VehicleNotAvailable", "detail": "Vehicle VEH-0001 is not available on 2026-10-14: it has another Trip that day." }""", "application/problem+json");

        var answer = await _client.CreateDraftTripAsync(new TripEntry(new DateOnly(2026, 10, 14), "VEH-0001", null), Cancel);

        answer.Should().Be(new TripAnswer.Refused("Vehicle VEH-0001 is not available on 2026-10-14: it has another Trip that day."));
        ScopeOf(_logger.LatestRecord).Should().Contain(new Dictionary<string, object?>
        {
            ["flow"] = "trip-planning",
            ["outcome"] = "Refused",
            ["failureCode"] = "VehicleNotAvailable",
        });
    }

    [Fact]
    public async Task ARefusalWithoutAProblem_SaysWhatWasRefused()
    {
        _tmsApi.Answer(HttpStatusCode.BadRequest, "{}");

        (await _client.DiscardDraftTripAsync("TR-0000001", Cancel)).Should().Be(new TripAnswer.Refused("Discard Draft Trip was refused."));
    }

    private static Dictionary<string, object?> ScopeOf(FakeLogRecord record) =>
        record.Scopes.OfType<IEnumerable<KeyValuePair<string, object?>>>().SelectMany(keys => keys).ToDictionary();

    [Fact]
    public async Task AnUnexpectedStatus_Throws()
    {
        _tmsApi.Answer(HttpStatusCode.InternalServerError, string.Empty);

        var act = () => _client.DiscardDraftTripAsync("TR-0000001", Cancel);

        await act.Should().ThrowAsync<HttpRequestException>();
    }
}
