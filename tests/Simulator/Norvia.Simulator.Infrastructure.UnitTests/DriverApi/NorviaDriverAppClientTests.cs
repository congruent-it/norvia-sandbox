using System.Diagnostics;
using System.Net;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging.Testing;
using Norvia.Simulator.Application.Ports;
using Norvia.Simulator.Domain.Log;
using Norvia.Simulator.Domain.Worlds;
using Norvia.Simulator.Infrastructure.DriverApi;
using Norvia.Simulator.Infrastructure.PartnerApi;
using Norvia.Simulator.Infrastructure.UnitTests.PartnerApi;

namespace Norvia.Simulator.Infrastructure.UnitTests.DriverApi;

public sealed class NorviaDriverAppClientTests : IDisposable
{
    private static readonly SimulatedDriver Sanne = new("DRV-0006", "Sanne Jansen", [LicenceCategory.B]);

    private readonly StubHandler _driverApp = new();
    private readonly IDriverTokens _tokens = Substitute.For<IDriverTokens>();
    private readonly FakeLogger<NorviaDriverAppClient> _logger = new();
    private readonly SimulatorActivities _activities = new("Norvia.Simulator.Web.Tests.DriverApp");
    private readonly List<Activity> _spans = [];
    private readonly ActivityListener _listener;
    private readonly HttpClient _http;
    private readonly NorviaDriverAppClient _client;

    public NorviaDriverAppClientTests()
    {
        _http = new HttpClient(_driverApp) { BaseAddress = new Uri("https://driver.example/") };
        _tokens.GetTokenAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(call => $"token-of-{call.ArgAt<string>(0)}");
        _client = new NorviaDriverAppClient(_http, _tokens, _activities, _logger);
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
        _driverApp.Dispose();
    }

    private void Takes(string correlationId = "drv-1") =>
        _driverApp.Answer(HttpStatusCode.OK, $$"""{"reportId":"3f2a4c9e-6c4a-4d0e-9c2a-0b1f6f0e8a11","correlationId":"{{correlationId}}","alreadyReported":false}""");

    [Fact]
    public async Task GetTrips_AsTheDriver_ReadsTheirTrips()
    {
        _driverApp.Answer(HttpStatusCode.OK, """[{"tripNumber":"TR-1","version":1,"status":"In progress","reports":[]}]""");

        var trips = await _client.GetTripsAsync(Sanne, CancellationToken.None);

        _driverApp.Request!.Method.Should().Be(HttpMethod.Get);
        _driverApp.Request.RequestUri.Should().Be(new Uri("https://driver.example/v1/driver/trips"));
        _driverApp.Request.Headers.Authorization!.Parameter.Should().Be("token-of-DRV-0006");
        trips.Should().Equal(new DriverTripSnapshot("TR-1", 1, DriverTripStatus.InProgress, 0));
    }

    [Fact]
    public async Task GetTrips_ADriverAppThatAnswersAnError_GivesNull()
    {
        _driverApp.Answer(HttpStatusCode.ServiceUnavailable, "{}");

        (await _client.GetTripsAsync(Sanne, CancellationToken.None)).Should().BeNull();
        _logger.Collector.GetSnapshot().Should().Contain(record => record.Level == Microsoft.Extensions.Logging.LogLevel.Warning);
    }

    [Fact]
    public async Task GetTrips_ADriverAppThatCannotBeReached_GivesNull()
    {
        _driverApp.Fail(new HttpRequestException("down"));

        (await _client.GetTripsAsync(Sanne, CancellationToken.None)).Should().BeNull();
    }

    [Fact]
    public async Task StartTrip_IsPostedAsTheDriver_AndGivesTheTrailOfTheirAction()
    {
        Takes("drv-start");

        var answer = await _client.StartTripAsync(Sanne, "TR-0000012", CancellationToken.None);

        _driverApp.Request!.Method.Should().Be(HttpMethod.Post);
        _driverApp.Request.RequestUri.Should().Be(new Uri("https://driver.example/v1/driver/trips/TR-0000012/start"));
        _driverApp.Request.Headers.Authorization!.Parameter.Should().Be("token-of-DRV-0006");
        answer.Should().Be(new DriverCallAnswer(LogOutcome.DriverReported, "drv-start", null));
    }

    [Fact]
    public async Task EndTrip_IsPostedToTheTripsEnd()
    {
        Takes();

        await _client.EndTripAsync(Sanne, "TR-0000012", CancellationToken.None);

        _driverApp.Request!.RequestUri.Should().Be(new Uri("https://driver.example/v1/driver/trips/TR-0000012/end"));
    }

    [Theory]
    [InlineData("collect", "collection/collected", null)]
    [InlineData("failCollection", "collection/collection-failed", "Nobody there")]
    [InlineData("failDelivery", "delivery/delivery-failed", "Closed")]
    [InlineData("damageAtCollection", "collection/damaged", "A pallet was damaged")]
    [InlineData("damageAtDelivery", "delivery/damaged", "A pallet was damaged")]
    public async Task AReportOnAStop_GoesToTheStopsKindAndAction_WithTheRemark(string report, string route, string? remark)
    {
        Takes();

        _ = report switch
        {
            "collect" => await _client.CollectAsync(Sanne, "TR-1", "SH-0000002", CancellationToken.None),
            "failCollection" => await _client.FailCollectionAsync(Sanne, "TR-1", "SH-0000002", remark!, CancellationToken.None),
            "failDelivery" => await _client.FailDeliveryAsync(Sanne, "TR-1", "SH-0000002", remark!, CancellationToken.None),
            "damageAtCollection" => await _client.ReportDamageAsync(Sanne, "TR-1", "SH-0000002", atDelivery: false, remark!, CancellationToken.None),
            _ => await _client.ReportDamageAsync(Sanne, "TR-1", "SH-0000002", atDelivery: true, remark!, CancellationToken.None),
        };

        _driverApp.Request!.RequestUri.Should().Be(new Uri($"https://driver.example/v1/driver/trips/TR-1/stops/SH-0000002/{route}"));
        JsonNode.Parse(_driverApp.RequestBody!)!["remark"]?.GetValue<string>().Should().Be(remark);
    }

    [Theory]
    [InlineData(false, "Clean", null)]
    [InlineData(true, "Reservations", "One carton was dented")]
    public async Task Deliver_SendsTheProofOfDeliveryForm_WithTheSignatureAndPhotosItIsGiven(bool reservations, string condition, string? remark)
    {
        Takes();

        await _client.DeliverAsync(Sanne, "TR-1", "SH-0000002", new DriverDelivery("Simulated consignee", reservations, remark, Png(1), reservations ? [Png(2)] : []), CancellationToken.None);

        _driverApp.Request!.RequestUri.Should().Be(new Uri("https://driver.example/v1/driver/trips/TR-1/stops/SH-0000002/delivery/deliver"));
        var sent = JsonNode.Parse(_driverApp.RequestBody!)!;
        sent["receivedBy"]!.GetValue<string>().Should().Be("Simulated consignee");
        sent["condition"]!.GetValue<string>().Should().Be(condition);
        sent["remark"]?.GetValue<string>().Should().Be(remark);
        sent["signature"]!["contentType"]!.GetValue<string>().Should().Be("image/png");
        Convert.FromBase64String(sent["signature"]!["content"]!.GetValue<string>()).Should().Equal(Png(1));
        sent["photos"]!.AsArray().Select(photo => photo!["contentType"]!.GetValue<string>()).Should().HaveCount(reservations ? 1 : 0).And.OnlyContain(type => type == "image/png");
    }

    private static byte[] Png(byte marker) => [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, marker];

    [Fact]
    public async Task ARefusal_IsAnOutcomeNotAnError()
    {
        _driverApp.Answer(HttpStatusCode.Conflict, """{"title":"The Stop can't be completed now.","code":"ReportNotAvailable"}""");

        var answer = await _client.CollectAsync(Sanne, "TR-1", "SH-1", CancellationToken.None);

        answer.Outcome.Should().Be(LogOutcome.DriverRefused);
        answer.Detail.Should().Be("ReportNotAvailable (409)");
    }

    [Fact]
    public async Task TheDriverAppUnreachable_IsNotReceived()
    {
        _driverApp.Fail(new HttpRequestException("down"));

        var answer = await _client.StartTripAsync(Sanne, "TR-1", CancellationToken.None);

        answer.Outcome.Should().Be(LogOutcome.DriverNotReceived);
        _logger.Collector.GetSnapshot().Should().Contain(record => record.Level == Microsoft.Extensions.Logging.LogLevel.Warning);
    }

    [Fact]
    public async Task NoToken_IsNotReceived_AndNothingIsSent()
    {
        _tokens.GetTokenAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns<string>(_ => throw new InvalidOperationException("DriverApp:LocalTokens isn't configured."));

        var answer = await _client.StartTripAsync(Sanne, "TR-1", CancellationToken.None);

        answer.Outcome.Should().Be(LogOutcome.DriverNotReceived);
        _driverApp.Request.Should().BeNull();
    }

    [Theory]
    [InlineData("Start Trip")]
    [InlineData("End Trip")]
    public async Task TripReports_AreSentInASpanNamedLikeTheDriverAppsActions(string action)
    {
        Takes();

        _ = action == "Start Trip" ? await _client.StartTripAsync(Sanne, "TR-1", CancellationToken.None) : await _client.EndTripAsync(Sanne, "TR-1", CancellationToken.None);

        _spans.Should().ContainSingle().Which.OperationName.Should().Be(action);
    }

    [Fact]
    public async Task AReport_IsLoggedWithTheKeysTheSimulatorKnows()
    {
        Takes("drv-7");

        await _client.CollectAsync(Sanne, "TR-1", "SH-0000002", CancellationToken.None);

        var scope = _logger.Collector.GetSnapshot().Single(record => record.Message.Contains("answered")).Scopes.Single() as IEnumerable<KeyValuePair<string, object?>>;
        scope!.ToDictionary().Should().BeEquivalentTo(new Dictionary<string, object?>
        {
            ["flow"] = "trip-execution",
            ["tripNumber"] = "TR-1",
            ["partyId"] = "DRV-0006",
            ["shipmentNumber"] = "SH-0000002",
            ["correlationId"] = "drv-7",
            ["outcome"] = "Succeeded",
        });
    }

    [Fact]
    public async Task ARefusedReport_IsLoggedAsRefusedWithItsCode()
    {
        _driverApp.Answer(HttpStatusCode.NotFound, """{"title":"x","code":"StopNotFound"}""");

        await _client.CollectAsync(Sanne, "TR-1", "SH-0000002", CancellationToken.None);

        var scope = _logger.Collector.GetSnapshot().Single(record => record.Message.Contains("answered")).Scopes.Single() as IEnumerable<KeyValuePair<string, object?>>;
        scope!.ToDictionary().Should().Contain(new KeyValuePair<string, object?>("outcome", "Refused")).And.Contain(new KeyValuePair<string, object?>("failureCode", "StopNotFound"));
    }

    [Fact]
    public async Task AReportThatNeverArrived_IsLoggedAsFailed()
    {
        _driverApp.Fail(new HttpRequestException("down"));

        await _client.StartTripAsync(Sanne, "TR-1", CancellationToken.None);

        var scope = _logger.Collector.GetSnapshot().Single(record => record.Message.Contains("answered")).Scopes.Single() as IEnumerable<KeyValuePair<string, object?>>;
        scope!.ToDictionary().Should().Contain(new KeyValuePair<string, object?>("outcome", "Failed")).And.NotContainKey("correlationId");
    }
}
