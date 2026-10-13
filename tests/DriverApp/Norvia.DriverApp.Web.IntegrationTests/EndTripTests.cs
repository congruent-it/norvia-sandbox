using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;
using Norvia.DriverApp.Application.Events;
using Norvia.DriverApp.Domain.Reports;
using Norvia.DriverApp.Domain.Trips;

namespace Norvia.DriverApp.Web.IntegrationTests;

/// <summary>
/// The Driver ends a Trip in the Driver app (P12): End Trip is unavailable while a Stop is open, and pressing it publishes exactly one <c>TripEndReported</c>
/// carrying the Driver ID from the token (ADR 0016). Signed in with the local stand-in, with the clock on Tuesday 13 October 2026.
/// </summary>
public sealed partial class EndTripTests
{
    private static readonly WebApplicationFactoryClientOptions Browser = new() { AllowAutoRedirect = false, HandleCookies = true };
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    private static void DeliveryFailedEverywhere(DriverAppWebFactory app)
    {
        app.StartAnouksTrip();
        app.CollectAnouksShipments();
        foreach (var shipment in new[] { "SH-0000001", "SH-0000002" })
        {
            var failed = DriverReport.ReportStop(
                new ReportId(Guid.NewGuid()), ReportKind.DeliveryFailed, new StopRef(shipment, StopKind.Delivery),
                new ReportOrigin(new TripNumber("TR-0000002"), new DriverId(DriverAppWebFactory.AnoukId), 1, app.Time.GetUtcNow(), "deliver-corr"), "Closed");
            failed.Record(app.Time.GetUtcNow());
            app.Reports.Add(failed);
        }
    }

    [Fact]
    public async Task WhileAStopIsOpen_EndTripIsUnavailable_AndSaysWhy()
    {
        await using var app = new DriverAppWebFactory("Development", standIn: true);
        app.StartAnouksTrip();
        using var client = await SignedIn(app, DriverAppWebFactory.AnoukId);

        var page = await GetAsync(client, "/");

        page.Should().NotContain("aria-label=\"End Trip TR-0000002\"");
        page.Should().Contain("End Trip TR-0000002 is unavailable").And.Contain("Give every Stop an outcome first.");
    }

    [Fact]
    public async Task WhenEveryStopHasAnOutcome_EndTripIsOffered_AndPressingItPublishesExactlyOneEvent_WithTheDriverIdFromTheToken()
    {
        await using var app = new DriverAppWebFactory("Development", standIn: true);
        DeliveryFailedEverywhere(app);
        using var client = await SignedIn(app, DriverAppWebFactory.AnoukId);
        var before = await GetAsync(client, "/");

        var ended = await EndAsync(client, "TR-0000002", before);
        var page = await GetAsync(client, "/");

        before.Should().Contain("aria-label=\"End Trip TR-0000002\"");
        ended.StatusCode.Should().Be(HttpStatusCode.Redirect);
        var published = app.Saved.Should().ContainSingle().Which.Should().BeOfType<TripEndReportedV1>().Subject;
        published.DriverId.Should().Be(DriverAppWebFactory.AnoukId);
        published.TripNumber.Should().Be("TR-0000002");
        app.Reports.Last().Kind.Should().Be(ReportKind.EndTrip);
        app.Reports.Last().State.Should().Be(ReportState.Sending);
        page.Should().NotContain("aria-label=\"End Trip TR-0000002\"", "the Driver can't end it twice").And.Contain("http-equiv=\"refresh\"");
    }

    [Fact]
    public async Task ARecordedEnd_ShowsTheTripCompleted()
    {
        await using var app = new DriverAppWebFactory("Development", standIn: true);
        DeliveryFailedEverywhere(app);
        using var client = await SignedIn(app, DriverAppWebFactory.AnoukId);
        await EndAsync(client, "TR-0000002", await GetAsync(client, "/"));
        app.Reports.Last().Record(app.Time.GetUtcNow());

        var page = await GetAsync(client, "/");

        page.Should().Contain("Completed").And.Contain("End Trip").And.Contain("Recorded").And.NotContain("http-equiv=\"refresh\"");
    }

    [Fact]
    public async Task ARefusedEnd_ShowsStopsOpenFromTheTms_AndCanBeSentAgain()
    {
        await using var app = new DriverAppWebFactory("Development", standIn: true);
        DeliveryFailedEverywhere(app);
        using var client = await SignedIn(app, DriverAppWebFactory.AnoukId);
        await EndAsync(client, "TR-0000002", await GetAsync(client, "/"));
        app.Reports.Last().Refuse("StopsOpen", "Trip TR-0000002 still has open Stops.", app.Time.GetUtcNow());

        var page = await GetAsync(client, "/");

        page.Should().Contain("Refused").And.Contain("StopsOpen").And.Contain("aria-label=\"End Trip TR-0000002\"");
    }

    [Fact]
    public async Task EndingWhileAStopIsOpen_PublishesNothing()
    {
        await using var app = new DriverAppWebFactory("Development", standIn: true);
        app.StartAnouksTrip();
        using var client = await SignedIn(app, DriverAppWebFactory.AnoukId);

        // The Trip screen offers no End Trip form to take a token from, so the Driver forges the post with a token of the Collection Stop's form.
        var ended = await EndAsync(client, "TR-0000002", await GetAsync(client, "/trips/TR-0000002/stops/SH-0000001/collection"));

        ended.StatusCode.Should().Be(HttpStatusCode.Redirect);
        app.Saved.Should().BeEmpty();
        app.Reports.Should().ContainSingle(report => report.Kind == ReportKind.StartTrip);
    }

    [Fact]
    public async Task EndingATripOfAnotherDriver_IsNotFound_AndPublishesNothing()
    {
        await using var app = new DriverAppWebFactory("Development", standIn: true);
        using var client = await SignedIn(app, DriverAppWebFactory.AnoukId);

        var ended = await EndAsync(client, "TR-0000003", await GetAsync(client, "/"));

        ended.StatusCode.Should().Be(HttpStatusCode.NotFound);
        app.Saved.Should().BeEmpty();
    }

    [Fact]
    public async Task APostThatNamesAnotherDriver_IsStillTheSignedInDriversEnd()
    {
        await using var app = new DriverAppWebFactory("Development", standIn: true);
        DeliveryFailedEverywhere(app);
        using var client = await SignedIn(app, DriverAppWebFactory.AnoukId);
        var token = TokenOf(await GetAsync(client, "/"));

        using var post = await client.PostAsync(
            new Uri("/trips/TR-0000002/end", UriKind.Relative),
            new FormUrlEncodedContent([new("__RequestVerificationToken", token), new("driverId", DriverAppWebFactory.JoostId)]),
            Ct);

        post.StatusCode.Should().Be(HttpStatusCode.Redirect);
        app.Saved.Should().ContainSingle().Which.DriverId.Should().Be(DriverAppWebFactory.AnoukId);
    }

    [Fact]
    public async Task EndingWithoutAnAntiforgeryToken_IsRefused_AndPublishesNothing()
    {
        await using var app = new DriverAppWebFactory("Development", standIn: true);
        DeliveryFailedEverywhere(app);
        using var client = await SignedIn(app, DriverAppWebFactory.AnoukId);

        using var withoutToken = await client.PostAsync(new Uri("/trips/TR-0000002/end", UriKind.Relative), new FormUrlEncodedContent([]), Ct);

        withoutToken.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        app.Saved.Should().BeEmpty();
    }

    private static async Task<HttpClient> SignedIn(DriverAppWebFactory app, string driverId)
    {
        var client = app.CreateClient(Browser);
        using var signedIn = await client.PostAsync(new Uri("/local/sign-in", UriKind.Relative), new FormUrlEncodedContent([new("driver", driverId)]), Ct);
        signedIn.StatusCode.Should().Be(HttpStatusCode.Redirect);
        return client;
    }

    private static Task<string> GetAsync(HttpClient client, string path) => client.GetStringAsync(new Uri(path, UriKind.Relative), Ct);

    private static async Task<HttpResponseMessage> EndAsync(HttpClient client, string tripNumber, string page) =>
        await client.PostAsync(
            new Uri($"/trips/{tripNumber}/end", UriKind.Relative),
            new FormUrlEncodedContent([new("__RequestVerificationToken", TokenOf(page))]),
            Ct);

    /// <summary>The antiforgery token the page's form carries, which the browser would post back.</summary>
    private static string TokenOf(string page) =>
        AntiforgeryToken().Match(page) is { Success: true } match
            ? WebUtility.HtmlDecode(match.Groups[1].Value)
            : throw new InvalidOperationException("The page has no form with an antiforgery token.");

    [GeneratedRegex("name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex AntiforgeryToken();
}
