using System.Net;
using Microsoft.Extensions.Options;
using Norvia.IntegrationConsole.Application.Trails;
using Norvia.IntegrationConsole.Infrastructure.Trails;

namespace Norvia.IntegrationConsole.Infrastructure.UnitTests;

/// <summary>
/// Locally the console searches the telemetry the Aspire dashboard holds (ADR 0027), and answers as the Log Analytics
/// adapter does: the Message trails that carried the key in the range, reduced to the tracking contract's keys.
/// </summary>
public sealed class DashboardTrailSourceTests : IDisposable
{
    private const string Booked = "5b0d7f8e-2c41-4a3e-9d6a-0f1e2d3c4b5a";
    private const string Other = "9a8b7c6d-5e4f-4a3b-8c2d-1e0f9a8b7c6d";
    private const string Trace = "4bf92f3577b34da6a3ce929d0e0e4736";

    // OTLP's span kinds.
    private const int Server = 2;
    private const int Client = 3;

    private static readonly DateTimeOffset Now = new(2026, 9, 26, 10, 0, 0, TimeSpan.Zero);

    private readonly DashboardLogs _dashboard = new();
    private readonly HttpClient _http;

    public DashboardTrailSourceTests()
    {
        _http = new HttpClient(_dashboard) { BaseAddress = new Uri("http://localhost:15180") };
    }

    public void Dispose() => _http.Dispose();

    [Fact]
    public async Task SearchAsync_ByTransportOrderNumber_FindsTheTrailThatCarriedIt_WithEveryHopsKeys()
    {
        _dashboard
            .Add("partner-portal-web", Now.AddMinutes(-3), new() { ["correlationId"] = Booked, ["flow"] = "transport-order-intake", ["partyId"] = "SHP-0001", ["transportOrderNumber"] = "TO-0000042" })
            .Add("partner-api", Now.AddMinutes(-4), new() { ["correlationId"] = Booked, ["flow"] = "transport-order-intake", ["partyId"] = "SHP-0001", ["outcome"] = "Succeeded", ["transportOrderNumber"] = "TO-0000042" })
            .Add("tms-api", Now.AddMinutes(-5), new() { ["correlationId"] = Booked, ["flow"] = "transport-order-intake" })
            .Add("tms-api", Now.AddMinutes(-2), new() { ["correlationId"] = Other, ["flow"] = "transport-order-intake", ["transportOrderNumber"] = "TO-0000043" });

        var trails = await SearchAsync("TO-0000042");

        trails.Should().ContainSingle().Which.Should().BeEquivalentTo(new FoundTrail(
            Booked,
            ["transport-order-intake"],
            Now.AddMinutes(-5),
            Now.AddMinutes(-3),
            [Outcome.Succeeded],
            [new BusinessKey(BusinessKeyKind.TransportOrderNumber, "TO-0000042")]));
    }

    [Fact]
    public async Task SearchAsync_ByCorrelationIdInAnotherCase_FindsItsTrail()
    {
        _dashboard.Add("partner-api", Now.AddMinutes(-4), new() { ["correlationId"] = Booked, ["flow"] = "transport-order-intake", ["outcome"] = "Refused", ["failureCode"] = "NotAllowedToSubmit" });

        var trails = await SearchAsync(Booked.ToUpperInvariant());

        trails.Should().ContainSingle().Which.Outcomes.Should().Equal(Outcome.Refused);
    }

    [Fact]
    public async Task SearchAsync_KeyOnlyInALogMessage_FindsNothing_BecauseOnlyKeysAreRead()
    {
        _dashboard.Add("tms-api", Now.AddMinutes(-4), new() { ["correlationId"] = Booked, ["flow"] = "transport-order-intake" }, body: "Transport Order TO-0000042 Accepted");

        var trails = await SearchAsync("TO-0000042");

        trails.Should().BeEmpty();
    }

    [Fact]
    public async Task SearchAsync_HopsOutsideTheRange_AreLeftOut()
    {
        _dashboard
            .Add("partner-api", Now.AddHours(-3), new() { ["correlationId"] = Booked, ["flow"] = "transport-order-intake", ["transportOrderNumber"] = "TO-0000042" })
            .Add("tms-api", Now.AddMinutes(-30), new() { ["correlationId"] = Booked, ["flow"] = "transport-order-intake", ["transportOrderNumber"] = "TO-0000042" })
            .Add("tms-api", Now.AddHours(-5), new() { ["correlationId"] = Other, ["transportOrderNumber"] = "TO-0000042" });

        var trails = await SearchAsync("TO-0000042", within: TimeSpan.FromHours(1));

        var trail = trails.Should().ContainSingle().Which;
        trail.CorrelationId.Should().Be(Booked);
        trail.FirstSeen.Should().Be(Now.AddMinutes(-30));
    }

    [Fact]
    public async Task SearchAsync_ManyTrails_ReturnsTheLatestFirst_AtMostTheMaximum()
    {
        for (var trail = 0; trail < FoundTrails.Maximum + 1; trail++)
        {
            _dashboard.Add("tms-api", Now.AddSeconds(-trail), new() { ["correlationId"] = $"trail-{trail}", ["transportOrderNumber"] = "TO-0000042" });
        }

        var trails = await SearchAsync("TO-0000042");

        trails.Should().HaveCount(FoundTrails.Maximum);
        trails[0].CorrelationId.Should().Be("trail-0");
    }

    [Fact]
    public async Task SearchAsync_AsksTheTelemetryApi_ForEveryLogRecordItHolds_WithTheApiKey()
    {
        await SearchAsync("TO-0000042");

        var request = _dashboard.Requests.Should().ContainSingle().Which;
        request.RequestUri!.PathAndQuery.Should().Be($"/api/telemetry/logs?limit={DashboardTrailSource.MaximumLogRecords}");
        request.Headers.GetValues("x-api-key").Should().Equal("dashboard-api-key");
    }

    [Fact]
    public async Task SearchAsync_TheApiUnsecured_SendsNoApiKey()
    {
        await SearchAsync("TO-0000042", apiKey: null);

        _dashboard.Requests.Should().ContainSingle().Which.Headers.Contains("x-api-key").Should().BeFalse();
    }

    [Fact]
    public async Task SearchAsync_TheDashboardRefuses_Throws_SoTheSearchPageSaysItCouldntBeRun()
    {
        _dashboard.Status = HttpStatusCode.Unauthorized;

        var search = () => SearchAsync("TO-0000042");

        await search.Should().ThrowAsync<HttpRequestException>();
    }

    [Fact]
    public async Task HopsAsync_ForABooking_ReturnsOneHopPerCall_EachCausedByTheCallThatMadeIt()
    {
        // The stand-in's request calls the TMS; the TMS logs inside its request and inside a database call in it.
        _dashboard
            .AddSpan("partner-api", Trace, "a1a1a1a1a1a1a1a1", null, "POST /partner/v1/transport-orders", Server, Now.AddSeconds(-10), Now.AddSeconds(-9))
            .AddSpan("partner-api", Trace, "c1c1c1c1c1c1c1c1", "a1a1a1a1a1a1a1a1", "POST", Client, Now.AddMilliseconds(-9800), Now.AddMilliseconds(-9200))
            .AddSpan("tms-api", Trace, "b2b2b2b2b2b2b2b2", "c1c1c1c1c1c1c1c1", "POST /v1/transport-orders", Server, Now.AddMilliseconds(-9700), Now.AddMilliseconds(-9300))
            .AddSpan("tms-api", Trace, "d3d3d3d3d3d3d3d3", "b2b2b2b2b2b2b2b2", "INSERT norvia", Client, Now.AddMilliseconds(-9600), Now.AddMilliseconds(-9500))
            .Add("partner-api", Now.AddMilliseconds(-9100), new() { ["correlationId"] = Booked, ["flow"] = "transport-order-intake", ["outcome"] = "Succeeded", ["transportOrderNumber"] = "TO-0000042" }, traceId: Trace, spanId: "a1a1a1a1a1a1a1a1")
            .Add("tms-api", Now.AddMilliseconds(-9650), new() { ["correlationId"] = Booked, ["flow"] = "transport-order-intake" }, traceId: Trace, spanId: "b2b2b2b2b2b2b2b2")
            .Add("tms-api", Now.AddMilliseconds(-9550), new() { ["correlationId"] = Booked, ["transportOrderNumber"] = "TO-0000042" }, traceId: Trace, spanId: "d3d3d3d3d3d3d3d3");

        var hops = await HopsAsync(Booked);

        hops.Should().BeEquivalentTo(
        [
            new RecordedHop(
                "a1a1a1a1a1a1a1a1", null, "partner-api: POST /partner/v1/transport-orders", "transport-order-intake",
                Now.AddSeconds(-10), TimeSpan.FromSeconds(1), Outcome.Succeeded, null, [new BusinessKey(BusinessKeyKind.TransportOrderNumber, "TO-0000042")]),
            new RecordedHop(
                "b2b2b2b2b2b2b2b2", "a1a1a1a1a1a1a1a1", "tms-api: POST /v1/transport-orders", "transport-order-intake",
                Now.AddMilliseconds(-9700), TimeSpan.FromMilliseconds(400), null, null, [new BusinessKey(BusinessKeyKind.TransportOrderNumber, "TO-0000042")]),
        ]);
    }

    [Fact]
    public async Task HopsAsync_AsksForTheSpansOfEachTraceTheTrailWasRecordedIn_WithTheApiKey()
    {
        _dashboard
            .Add("partner-api", Now.AddMinutes(-2), new() { ["correlationId"] = Booked }, traceId: Trace, spanId: "a1a1a1a1a1a1a1a1")
            .Add("tms-api", Now.AddMinutes(-1), new() { ["correlationId"] = Booked }, traceId: Trace, spanId: "b2b2b2b2b2b2b2b2");

        await HopsAsync(Booked);

        var traces = _dashboard.Requests.Where(request => request.RequestUri!.AbsolutePath.StartsWith("/api/telemetry/traces/", StringComparison.Ordinal));
        traces.Should().ContainSingle().Which.RequestUri!.AbsolutePath.Should().Be($"/api/telemetry/traces/{Trace}");
        traces.Single().Headers.GetValues("x-api-key").Should().Equal("dashboard-api-key");
    }

    [Fact]
    public async Task HopsAsync_OtherTrailsAndRecordsOutsideTheRange_AreLeftOut()
    {
        _dashboard
            .Add("tms-api", Now.AddMinutes(-2), new() { ["correlationId"] = Other })
            .Add("tms-api", Now.AddHours(-3), new() { ["correlationId"] = Booked })
            .Add("tms-api", Now.AddMinutes(-1), new() { ["correlationId"] = Booked.ToUpperInvariant() });

        var hops = await HopsAsync(Booked, within: TimeSpan.FromHours(1));

        hops.Should().ContainSingle().Which.Time.Should().Be(Now.AddMinutes(-1));
    }

    [Fact]
    public async Task HopsAsync_ARecordWrittenOutsideAnySpan_IsAHopOfItsOwn_AtItsTime()
    {
        // The TMS's outbox logs a published event after the send's span has ended.
        _dashboard
            .Add("tms-api", Now.AddSeconds(-5), new() { ["correlationId"] = Booked, ["flow"] = "transport-order-intake", ["outcome"] = "Succeeded" })
            .Add("tms-api", Now.AddSeconds(-4), new() { ["correlationId"] = Booked, ["flow"] = "transport-order-intake" });

        var hops = await HopsAsync(Booked);

        hops.Should().HaveCount(2).And.OnlyContain(hop => hop.Name == "tms-api" && hop.Duration == null && hop.CausedBy == null);
        hops.Select(hop => hop.Time).Should().BeEquivalentTo([Now.AddSeconds(-5), Now.AddSeconds(-4)]);
        hops.Select(hop => hop.Id).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public async Task HopsAsync_ATraceTheDashboardNoLongerHolds_LeavesTheHopsAtTheirRecordsTimes()
    {
        _dashboard.Add("tms-api", Now.AddSeconds(-5), new() { ["correlationId"] = Booked }, traceId: Trace, spanId: "b2b2b2b2b2b2b2b2");

        var hops = await HopsAsync(Booked);

        var hop = hops.Should().ContainSingle().Subject;
        hop.Should().BeEquivalentTo(new { Name = "tms-api", Time = Now.AddSeconds(-5), Duration = (TimeSpan?)null, CausedBy = (string?)null });
    }

    [Fact]
    public async Task HopsAsync_AHopRecordingSeveralOutcomes_HasTheOneItEndedWith()
    {
        // The stand-in retried the TMS, which then took the booking: the hop succeeded.
        _dashboard
            .AddSpan("partner-api", Trace, "a1a1a1a1a1a1a1a1", null, "POST /partner/v1/transport-orders", Server, Now.AddSeconds(-10), Now.AddSeconds(-9))
            .Add("partner-api", Now.AddMilliseconds(-9200), new() { ["correlationId"] = Booked, ["outcome"] = "Succeeded" }, traceId: Trace, spanId: "a1a1a1a1a1a1a1a1")
            .Add("partner-api", Now.AddMilliseconds(-9800), new() { ["correlationId"] = Booked, ["outcome"] = "Retrying", ["failureCode"] = "TmsUnavailable" }, traceId: Trace, spanId: "a1a1a1a1a1a1a1a1");

        var hops = await HopsAsync(Booked);

        hops.Should().ContainSingle().Which.Should().BeEquivalentTo(new { Outcome = Outcome.Succeeded, FailureCode = (string?)null });
    }

    [Fact]
    public async Task HopsAsync_AHopThatEndedFailing_HasThatOutcome_WithItsFailureCode()
    {
        _dashboard
            .AddSpan("partner-api", Trace, "a1a1a1a1a1a1a1a1", null, "POST /partner/v1/transport-orders", Server, Now.AddSeconds(-10), Now.AddSeconds(-9))
            .Add("partner-api", Now.AddMilliseconds(-9800), new() { ["correlationId"] = Booked, ["outcome"] = "Retrying", ["failureCode"] = "TmsUnavailable" }, traceId: Trace, spanId: "a1a1a1a1a1a1a1a1")
            .Add("partner-api", Now.AddMilliseconds(-9500), new() { ["correlationId"] = Booked, ["outcome"] = "Failed", ["failureCode"] = "TmsCallRefused" }, traceId: Trace, spanId: "a1a1a1a1a1a1a1a1")
            .Add("partner-api", Now.AddMilliseconds(-9200), new() { ["correlationId"] = Booked, ["flow"] = "transport-order-intake" }, traceId: Trace, spanId: "a1a1a1a1a1a1a1a1");

        var hops = await HopsAsync(Booked);

        hops.Should().ContainSingle().Which.Should().BeEquivalentTo(new { Outcome = Outcome.Failed, FailureCode = "TmsCallRefused" });
    }

    [Fact]
    public async Task HopsAsync_TheDashboardRefuses_Throws_SoTheTrailPageSaysItCouldntBeOpened()
    {
        _dashboard.Status = HttpStatusCode.Unauthorized;

        var open = () => HopsAsync(Booked);

        await open.Should().ThrowAsync<HttpRequestException>();
    }

    private Task<IReadOnlyList<RecordedHop>> HopsAsync(string correlationId, TimeSpan? within = null) =>
        Source().HopsAsync(new TrailLookup(correlationId, Now - (within ?? TimeSpan.FromHours(24)), Now), TestContext.Current.CancellationToken);

    private DashboardTrailSource Source(string? apiKey = "dashboard-api-key") =>
        new(_http, Options.Create(new DashboardTrailSourceOptions { DashboardUrl = _http.BaseAddress, ApiKey = apiKey }));

    private Task<IReadOnlyList<FoundTrail>> SearchAsync(string key, TimeSpan? within = null, string? apiKey = "dashboard-api-key") =>
        Source(apiKey).SearchAsync(new TrailSearch(key, Now - (within ?? TimeSpan.FromHours(24)), Now), TestContext.Current.CancellationToken);
}
