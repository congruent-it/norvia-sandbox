using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;
using Norvia.DriverApp.Application.Events;
using Norvia.DriverApp.Domain.Reports;
using Norvia.DriverApp.Domain.Trips;

namespace Norvia.DriverApp.Web.IntegrationTests;

/// <summary>
/// The Driver starts a Trip in the Driver app (P12): exactly one <c>TripStartReported</c> carrying the Driver ID from the token, and the Trip screen shows
/// the report as Sending, Recorded, Refused or Not recorded (ADR 0016). Signed in with the local stand-in, with the clock on Tuesday 13 October 2026.
/// </summary>
public sealed partial class StartTripTests
{
    private static readonly WebApplicationFactoryClientOptions Browser = new() { AllowAutoRedirect = false, HandleCookies = true };
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    [Fact]
    public async Task AnUnstartedTripOnItsDay_OffersStartTrip_AndAnotherDaysTripDoesNot()
    {
        await using var app = new DriverAppWebFactory("Development", standIn: true);
        using var anouk = await SignedIn(app, DriverAppWebFactory.AnoukId);
        using var joost = await SignedIn(app, DriverAppWebFactory.JoostId);

        var anoukPage = await GetAsync(anouk, "/");
        var joostPage = await GetAsync(joost, "/");

        anoukPage.Should().Contain("Start Trip TR-0000002").And.NotContain("Start Trip TR-0000001");
        joostPage.Should().NotContain("Start Trip TR-0000003", "that Trip is for tomorrow");
    }

    [Fact]
    public async Task PressingStartTrip_PublishesExactlyOneEvent_WithTheDriverIdFromTheToken_AndShowsTheReportAsSending()
    {
        await using var app = new DriverAppWebFactory("Development", standIn: true);
        using var client = await SignedIn(app, DriverAppWebFactory.AnoukId);

        var started = await StartAsync(client, "TR-0000002", await GetAsync(client, "/"));
        var page = await GetAsync(client, "/");

        started.StatusCode.Should().Be(HttpStatusCode.Redirect);
        var published = app.Saved.Should().ContainSingle().Which.Should().BeOfType<TripStartReportedV1>().Subject;
        published.DriverId.Should().Be(DriverAppWebFactory.AnoukId);
        published.TripNumber.Should().Be("TR-0000002");
        published.Version.Should().Be(1);
        var report = app.Reports.Should().ContainSingle().Subject;
        report.State.Should().Be(ReportState.Sending);
        page.Should().Contain("Sending").And.NotContain("Start Trip TR-0000002", "the Driver can't start it twice");
        page.Should().Contain("http-equiv=\"refresh\"", "a report waiting for an answer looks again by itself");
    }

    [Fact]
    public async Task APostThatNamesAnotherDriver_IsStillTheSignedInDriversStart()
    {
        await using var app = new DriverAppWebFactory("Development", standIn: true);
        using var client = await SignedIn(app, DriverAppWebFactory.AnoukId);
        var token = TokenOf(await GetAsync(client, "/"));

        using var post = await client.PostAsync(
            new Uri("/trips/TR-0000002/start", UriKind.Relative),
            new FormUrlEncodedContent([new("__RequestVerificationToken", token), new("driverId", DriverAppWebFactory.JoostId)]),
            Ct);

        post.StatusCode.Should().Be(HttpStatusCode.Redirect);
        app.Saved.Should().ContainSingle().Which.DriverId.Should().Be(DriverAppWebFactory.AnoukId);
    }

    [Fact]
    public async Task StartingATripOfAnotherDriver_IsNotFound_AndPublishesNothing()
    {
        await using var app = new DriverAppWebFactory("Development", standIn: true);
        using var client = await SignedIn(app, DriverAppWebFactory.AnoukId);

        var started = await StartAsync(client, "TR-0000003", await GetAsync(client, "/"));

        started.StatusCode.Should().Be(HttpStatusCode.NotFound);
        app.Saved.Should().BeEmpty();
        app.Reports.Should().BeEmpty();
    }

    [Fact]
    public async Task StartingWithoutAnAntiforgeryToken_AndWithoutASignIn_IsRefused_AndPublishesNothing()
    {
        await using var app = new DriverAppWebFactory("Development", standIn: true);
        using var client = await SignedIn(app, DriverAppWebFactory.AnoukId);
        using var stranger = app.CreateClient(Browser);

        using var withoutToken = await client.PostAsync(new Uri("/trips/TR-0000002/start", UriKind.Relative), new FormUrlEncodedContent([]), Ct);
        using var withoutSignIn = await stranger.PostAsync(new Uri("/trips/TR-0000002/start", UriKind.Relative), new FormUrlEncodedContent([]), Ct);

        app.Saved.Should().BeEmpty();
        withoutToken.StatusCode.Should().Be(HttpStatusCode.BadRequest, await withoutToken.Content.ReadAsStringAsync(Ct));
        withoutSignIn.StatusCode.Should().BeOneOf(HttpStatusCode.Redirect, HttpStatusCode.BadRequest);
        app.Saved.Should().BeEmpty();
    }

    [Theory]
    [InlineData(ReportState.Recorded, "Recorded", "In progress")]
    [InlineData(ReportState.Refused, "Refused", "NotTripDay")]
    [InlineData(ReportState.NotRecorded, "Not recorded", "TmsUnavailable")]
    public async Task TheReportShowsWhatBecameOfIt(ReportState state, string shown, string alsoShown)
    {
        await using var app = new DriverAppWebFactory("Development", standIn: true);
        using var client = await SignedIn(app, DriverAppWebFactory.AnoukId);
        await StartAsync(client, "TR-0000002", await GetAsync(client, "/"));
        var at = app.Time.GetUtcNow();
        _ = state switch
        {
            ReportState.Recorded => app.Reports.Single().Record(at),
            ReportState.Refused => app.Reports.Single().Refuse("NotTripDay", "Trip TR-0000002 is for another day.", at),
            _ => app.Reports.Single().GiveUp("TmsUnavailable", "The TMS answered 503 (4 attempts).", at),
        };

        var page = await GetAsync(client, "/");

        page.Should().Contain(shown).And.Contain(alsoShown).And.NotContain("http-equiv=\"refresh\"");
        if (state != ReportState.Recorded)
        {
            page.Should().Contain("Start Trip TR-0000002", "a refused or lost Start can be sent again");
        }
        else
        {
            page.Should().NotContain("Start Trip TR-0000002");
        }
    }

    private static async Task<HttpClient> SignedIn(DriverAppWebFactory app, string driverId)
    {
        var client = app.CreateClient(Browser);
        using var signedIn = await client.PostAsync(new Uri("/local/sign-in", UriKind.Relative), new FormUrlEncodedContent([new("driver", driverId)]), Ct);
        signedIn.StatusCode.Should().Be(HttpStatusCode.Redirect);
        return client;
    }

    private static Task<string> GetAsync(HttpClient client, string path) => client.GetStringAsync(new Uri(path, UriKind.Relative), Ct);

    private static async Task<HttpResponseMessage> StartAsync(HttpClient client, string tripNumber, string page) =>
        await client.PostAsync(
            new Uri($"/trips/{tripNumber}/start", UriKind.Relative),
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
