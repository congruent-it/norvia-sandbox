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

    private Task<IReadOnlyList<FoundTrail>> SearchAsync(string key, TimeSpan? within = null, string? apiKey = "dashboard-api-key") =>
        new DashboardTrailSource(_http, Options.Create(new DashboardTrailSourceOptions { DashboardUrl = _http.BaseAddress, ApiKey = apiKey }))
            .SearchAsync(new TrailSearch(key, Now - (within ?? TimeSpan.FromHours(24)), Now), TestContext.Current.CancellationToken);
}
