using System.Net;
using Microsoft.Extensions.Options;
using Norvia.IntegrationConsole.Application.Runs;
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
    private const int Internal = 1;
    private const int Server = 2;
    private const int Client = 3;
    private const int Producer = 4;
    private const int Consumer = 5;

    private static readonly DateTimeOffset Now = new(2026, 9, 26, 10, 0, 0, TimeSpan.Zero);

    private readonly DashboardLogs _dashboard = new();
    private readonly HttpClient _http;

    public DashboardTrailSourceTests()
    {
        _http = new HttpClient(_dashboard) { BaseAddress = new Uri("http://localhost:15180") };
    }

    public void Dispose() => _http.Dispose();

    [Fact]
    public async Task FailedRunsAsync_ForFailuresInTheWindow_ListsOneFailedRunPerFlowAndApplicationOfATrail_WithoutARun()
    {
        _dashboard
            .Add("partner-api", Now.AddMinutes(-9), new() { ["correlationId"] = Booked, ["flow"] = "transport-order-intake", ["outcome"] = "Failed", ["failureCode"] = "TmsUnavailable" })
            .Add("partner-api", Now.AddMinutes(-4), new() { ["correlationId"] = Booked, ["flow"] = "transport-order-intake", ["outcome"] = "Failed", ["failureCode"] = "TmsUnavailable" })
            .Add("tms-api", Now.AddMinutes(-3), new() { ["correlationId"] = Booked, ["flow"] = "transport-order-intake", ["outcome"] = "Succeeded" })
            .Add("partner-api", Now.AddMinutes(-2), new() { ["correlationId"] = Other, ["flow"] = "carrier-answer", ["outcome"] = "Refused", ["failureCode"] = "CallerNotAllowed" })
            .Add("partner-api", Now.AddHours(-30), new() { ["correlationId"] = Other, ["flow"] = "carrier-answer", ["outcome"] = "Failed" });

        var failed = await new DashboardTrailSource(_http, Options.Create(new DashboardTrailSourceOptions())).FailedRunsAsync(Now.AddHours(-24), TestContext.Current.CancellationToken);

        failed.Should().Equal(new RecordedFailedRun(null, Now.AddMinutes(-4), "transport-order-intake", Booked, null, new StepError("TmsUnavailable", null)));
    }

    [Fact]
    public async Task FailedRunsAsync_ForAFailureWithoutAFailureCode_HasNoError()
    {
        _dashboard.Add("partner-api", Now.AddMinutes(-4), new() { ["correlationId"] = Booked, ["outcome"] = "Failed" });

        var failed = await new DashboardTrailSource(_http, Options.Create(new DashboardTrailSourceOptions())).FailedRunsAsync(Now.AddHours(-24), TestContext.Current.CancellationToken);

        failed.Should().ContainSingle().Which.Should().Be(new RecordedFailedRun(null, Now.AddMinutes(-4), null, Booked, null, null));
    }

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
            .AddSpan("partner-api", Trace, "a1a1a1a1a1a1a1a1", null, "POST /partner/v1/transport-orders", Server, Now.AddSeconds(-10), Now.AddSeconds(-9),
                Called("POST", "/partner/v1/transport-orders"))
            .AddSpan("partner-api", Trace, "c1c1c1c1c1c1c1c1", "a1a1a1a1a1a1a1a1", "POST", Client, Now.AddMilliseconds(-9800), Now.AddMilliseconds(-9200))
            .AddSpan("tms-api", Trace, "b2b2b2b2b2b2b2b2", "c1c1c1c1c1c1c1c1", "POST /v{version:apiVersion}/transport-orders/", Server, Now.AddMilliseconds(-9700), Now.AddMilliseconds(-9300),
                Called("POST", "/v1/transport-orders"))
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
    public async Task HopsAsync_APlannersAssignment_IsNamedByThePathCalled_NotByItsRouteTemplate()
    {
        // ASP.NET Core names a request's span by its route template; the path actually called is its url.path.
        _dashboard
            .AddSpan("tms-api", Trace, "b2b2b2b2b2b2b2b2", null, "POST /v{version:apiVersion}/transport-orders/{transportOrderNumber}/assign", Server,
                Now.AddSeconds(-10), Now.AddSeconds(-9), Called("POST", "/v1/transport-orders/TO-0000042/assign"))
            .Add("tms-api", Now.AddMilliseconds(-9500), new() { ["correlationId"] = Booked, ["flow"] = "transport-planning", ["outcome"] = "Succeeded" }, traceId: Trace, spanId: "b2b2b2b2b2b2b2b2");

        var hops = await HopsAsync(Booked);

        hops.Should().ContainSingle().Which.Name.Should().Be("tms-api: POST /v1/transport-orders/TO-0000042/assign");
    }

    [Fact]
    public async Task HopsAsync_ACallFromABlazorEvent_IsNamedByTheApplicationsOwnSpan_NotByTheFrameworksEvent()
    {
        // A booking from the Partner portal: Blazor's span for the form's submit event, the portal's own span for sending
        // the booking, and HttpClient's call to the Partner API, which the stand-in serves.
        _dashboard
            .AddSpan("partner-portal-web", Trace, "e0e0e0e0e0e0e0e0", null, "Event onsubmit -> Microsoft.AspNetCore.Components.Forms.EditForm.HandleSubmitAsync", Internal,
                Now.AddSeconds(-11), Now.AddSeconds(-8), scope: "Microsoft.AspNetCore.Components")
            .AddSpan("partner-portal-web", Trace, "f1f1f1f1f1f1f1f1", "e0e0e0e0e0e0e0e0", "Send booking", Internal,
                Now.AddMilliseconds(-10_900), Now.AddMilliseconds(-8_500), scope: "Norvia.PartnerPortal.Web")
            .AddSpan("partner-portal-web", Trace, "c0c0c0c0c0c0c0c0", "f1f1f1f1f1f1f1f1", "POST", Client,
                Now.AddMilliseconds(-10_800), Now.AddMilliseconds(-8_600), scope: "System.Net.Http")
            .AddSpan("partner-api", Trace, "a1a1a1a1a1a1a1a1", "c0c0c0c0c0c0c0c0", "POST /partner/v1/transport-orders", Server,
                Now.AddSeconds(-10), Now.AddSeconds(-9), Called("POST", "/partner/v1/transport-orders"))
            .Add("partner-portal-web", Now.AddMilliseconds(-8_550), new() { ["correlationId"] = Booked, ["flow"] = "transport-order-intake", ["outcome"] = "Succeeded" },
                traceId: Trace, spanId: "f1f1f1f1f1f1f1f1", category: "Norvia.PartnerPortal.Infrastructure.PartnerApi.NorviaPartnerApiClient")
            .Add("partner-api", Now.AddMilliseconds(-9_100), new() { ["correlationId"] = Booked, ["outcome"] = "Succeeded" }, traceId: Trace, spanId: "a1a1a1a1a1a1a1a1");

        var hops = await HopsAsync(Booked);

        hops.Should().BeEquivalentTo(
        [
            new { Id = "f1f1f1f1f1f1f1f1", CausedBy = (string?)null, Name = "partner-portal-web: Send booking", Outcome = (Outcome?)Outcome.Succeeded },
            new { Id = "a1a1a1a1a1a1a1a1", CausedBy = (string?)"f1f1f1f1f1f1f1f1", Name = "partner-api: POST /partner/v1/transport-orders", Outcome = (Outcome?)Outcome.Succeeded },
        ]);
    }

    [Fact]
    public async Task HopsAsync_ARecordOnlyUnderAFrameworksSpan_IsNamedByThatSpan()
    {
        // Nothing of the application's own names the hop better.
        _dashboard
            .AddSpan("partner-portal-web", Trace, "e0e0e0e0e0e0e0e0", null, "Event onsubmit -> Microsoft.AspNetCore.Components.Forms.EditForm.HandleSubmitAsync", Internal,
                Now.AddSeconds(-11), Now.AddSeconds(-8), scope: "Microsoft.AspNetCore.Components")
            .Add("partner-portal-web", Now.AddSeconds(-9), new() { ["correlationId"] = Booked }, traceId: Trace, spanId: "e0e0e0e0e0e0e0e0");

        var hops = await HopsAsync(Booked);

        hops.Should().ContainSingle().Which.Name.Should().Be("partner-portal-web: Event onsubmit -> Microsoft.AspNetCore.Components.Forms.EditForm.HandleSubmitAsync");
    }

    [Fact]
    public async Task HopsAsync_ASpanWithoutAPathCalled_IsNamedBySpanName()
    {
        // A message received has no path, as a request has.
        _dashboard
            .AddSpan("tms-api", Trace, "b2b2b2b2b2b2b2b2", null, "transport-orders receive", Consumer, Now.AddSeconds(-10), Now.AddSeconds(-9))
            .Add("tms-api", Now.AddMilliseconds(-9500), new() { ["correlationId"] = Booked }, traceId: Trace, spanId: "b2b2b2b2b2b2b2b2");

        var hops = await HopsAsync(Booked);

        hops.Should().ContainSingle().Which.Name.Should().Be("tms-api: transport-orders receive");
    }

    [Fact]
    public async Task HopsAsync_AnEventTheOutboxPublished_IsAHopNamedAfterTheEvent_ThatSucceeded()
    {
        // The TMS's outbox logs a published event outside any span, in the event's application properties and its outcome.
        _dashboard.Add(
            "tms-api",
            Now.AddSeconds(-5),
            new()
            {
                ["correlationId"] = Booked,
                ["flow"] = "transport-planning",
                ["partyId"] = "SHP-0001",
                ["transportOrderNumber"] = "TO-0000042",
                ["shipmentNumber"] = "SHP-0000007",
                ["eventType"] = "ShipmentPlanned",
                ["eventVersion"] = "1",
                ["outcome"] = "Succeeded",
            },
            body: "Published ShipmentPlanned 0b6f3c1e-8a2d-4f5b-9c7e-1d2a3b4c5d6e for correlation ID " + Booked,
            category: "Norvia.Tms.Infrastructure.Outbox.OutboxPublisher");

        var hops = await HopsAsync(Booked);

        hops.Should().ContainSingle().Which.Should().BeEquivalentTo(new
        {
            Name = "tms-api: publish ShipmentPlanned",
            Flow = "transport-planning",
            Outcome = Outcome.Succeeded,
            Duration = (TimeSpan?)null,
        });
    }

    [Fact]
    public async Task HopsAsync_AnEventTheOutboxPublished_IsAHopOfItsPublishSpan_CausedByTheWorkThatRaisedIt()
    {
        // The TMS's outbox publishes in a span that continues the trace of what raised the event: the intake call raised
        // TransportOrderAccepted, and the rating worker's span, a trace of its own, TransportOrderRated.
        const string rating = "7c3e1f2a9b8d4c5e6f7a8b9c0d1e2f3a";
        _dashboard
            .AddSpan("tms-api", Trace, "b2b2b2b2b2b2b2b2", null, "POST /v{version:apiVersion}/transport-orders/", Server, Now.AddSeconds(-10), Now.AddSeconds(-8.4),
                Called("POST", "/v1/transport-orders"))
            .AddSpan("tms-api", Trace, "e1e1e1e1e1e1e1e1", "b2b2b2b2b2b2b2b2", "publish TransportOrderAccepted", Producer, Now.AddSeconds(-6), Now.AddSeconds(-5.9),
                scope: "Norvia.Tms.Api")
            .AddSpan("tms-api", rating, "r1r1r1r1r1r1r1r1", null, "Rate Transport Order", Internal, Now.AddSeconds(-8), Now.AddSeconds(-7.7), scope: "Norvia.Tms.Api")
            .AddSpan("tms-api", rating, "e2e2e2e2e2e2e2e2", "r1r1r1r1r1r1r1r1", "publish TransportOrderRated", Producer, Now.AddSeconds(-5.9), Now.AddSeconds(-5.8),
                scope: "Norvia.Tms.Api")
            .Add("tms-api", Now.AddSeconds(-9), new() { ["correlationId"] = Booked, ["flow"] = "transport-order-intake", ["outcome"] = "Succeeded" },
                traceId: Trace, spanId: "b2b2b2b2b2b2b2b2")
            .Add("tms-api", Now.AddSeconds(-5.95), new() { ["correlationId"] = Booked, ["eventType"] = "TransportOrderAccepted", ["outcome"] = "Succeeded" },
                traceId: Trace, spanId: "e1e1e1e1e1e1e1e1", category: "Norvia.Tms.Infrastructure.Outbox.OutboxPublisher")
            .Add("tms-api", Now.AddSeconds(-7.8), new() { ["correlationId"] = Booked, ["flow"] = "transport-order-rating", ["outcome"] = "Succeeded" },
                traceId: rating, spanId: "r1r1r1r1r1r1r1r1")
            .Add("tms-api", Now.AddSeconds(-5.85), new() { ["correlationId"] = Booked, ["eventType"] = "TransportOrderRated", ["outcome"] = "Succeeded" },
                traceId: rating, spanId: "e2e2e2e2e2e2e2e2", category: "Norvia.Tms.Infrastructure.Outbox.OutboxPublisher");

        var hops = await HopsAsync(Booked);

        hops.Should().BeEquivalentTo(
        [
            new { Id = "b2b2b2b2b2b2b2b2", CausedBy = (string?)null, Name = "tms-api: POST /v1/transport-orders" },
            new { Id = "e1e1e1e1e1e1e1e1", CausedBy = (string?)"b2b2b2b2b2b2b2b2", Name = "tms-api: publish TransportOrderAccepted" },
            new { Id = "r1r1r1r1r1r1r1r1", CausedBy = (string?)null, Name = "tms-api: Rate Transport Order" },
            new { Id = "e2e2e2e2e2e2e2e2", CausedBy = (string?)"r1r1r1r1r1r1r1r1", Name = "tms-api: publish TransportOrderRated" },
        ]);
        hops.Single(hop => hop.Id == "e1e1e1e1e1e1e1e1").Should().BeEquivalentTo(new
        {
            Time = Now.AddSeconds(-6),
            Duration = (TimeSpan?)TimeSpan.FromMilliseconds(100),
            Outcome = (Outcome?)Outcome.Succeeded,
        });
    }

    [Fact]
    public async Task HopsAsync_ARecordWrittenOutsideAnySpan_IsAHopOfItsOwn_AtItsTime()
    {
        // Records that say nothing of what happened, written outside any span, name only their application.
        _dashboard
            .Add("tms-api", Now.AddSeconds(-5), new() { ["correlationId"] = Booked, ["flow"] = "transport-order-intake", ["outcome"] = "Succeeded" })
            .Add("tms-api", Now.AddSeconds(-4), new() { ["correlationId"] = Booked, ["flow"] = "transport-order-intake" });

        var hops = await HopsAsync(Booked);

        hops.Should().HaveCount(2).And.OnlyContain(hop => hop.Name == "tms-api" && hop.Duration == null && hop.CausedBy == null);
        hops.Select(hop => hop.Time).Should().BeEquivalentTo([Now.AddSeconds(-5), Now.AddSeconds(-4)]);
        hops.Select(hop => hop.Id).Should().OnlyHaveUniqueItems();
    }

    [Theory]
    [InlineData("Azure.Identity")]
    [InlineData("Azure.Messaging.ServiceBus")]
    [InlineData("Microsoft.EntityFrameworkCore.Database.Command")]
    [InlineData("System.Net.Http.HttpClient.Default.LogicalHandler")]
    public async Task HopsAsync_ARecordAFrameworkWroteInTheLoggingScope_IsNoHop(string category)
    {
        // The TMS's outbox publishes in the intake's logging scope, so the Azure SDK's logs carry its correlation ID too.
        _dashboard
            .Add("tms-api", Now.AddMinutes(-4), new() { ["correlationId"] = Booked, ["flow"] = "transport-order-intake" }, category: "Norvia.Tms.Infrastructure.Outbox.OutboxPublisher")
            .Add("tms-api", Now.AddMinutes(-4).AddSeconds(1), new() { ["correlationId"] = Booked, ["flow"] = "transport-order-intake" }, category: category);

        var hops = await HopsAsync(Booked);

        hops.Should().ContainSingle().Which.Time.Should().Be(Now.AddMinutes(-4));
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

    // What ASP.NET Core records on a request's span besides its name.
    private static Dictionary<string, string> Called(string method, string path) =>
        new() { ["http.request.method"] = method, ["url.path"] = path };

    private Task<IReadOnlyList<RecordedHop>> HopsAsync(string correlationId, TimeSpan? within = null) =>
        Source().HopsAsync(new TrailLookup(correlationId, Now - (within ?? TimeSpan.FromHours(24)), Now), TestContext.Current.CancellationToken);

    private DashboardTrailSource Source(string? apiKey = "dashboard-api-key") =>
        new(_http, Options.Create(new DashboardTrailSourceOptions { DashboardUrl = _http.BaseAddress, ApiKey = apiKey }));

    private Task<IReadOnlyList<FoundTrail>> SearchAsync(string key, TimeSpan? within = null, string? apiKey = "dashboard-api-key") =>
        Source(apiKey).SearchAsync(new TrailSearch(key, Now - (within ?? TimeSpan.FromHours(24)), Now), TestContext.Current.CancellationToken);
}
