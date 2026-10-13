using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;
using Norvia.DriverApp.Application.Events;
using Norvia.DriverApp.Domain.Reports;

namespace Norvia.DriverApp.Web.IntegrationTests;

/// <summary>
/// The Driver reports on a Stop in the Driver app (P12): exactly one <c>StopCompletedReported</c> carrying the Driver ID from the token, and the Stop screen
/// shows the Shipment, the Shipper, the address, the window and the goods, and for a delivery the Consignee. A delivery is unavailable before its collection.
/// Signed in with the local stand-in, with the clock on Tuesday 13 October 2026 and Anouk's Trip TR-0000002 on its day.
/// </summary>
public sealed partial class StopReportTests
{
    private const string Collection = "/trips/TR-0000002/stops/SH-0000001/collection";
    private const string Delivery = "/trips/TR-0000002/stops/SH-0000001/delivery";

    private static readonly WebApplicationFactoryClientOptions Browser = new() { AllowAutoRedirect = false, HandleCookies = true };
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    [Fact]
    public async Task TheTripScreen_ListsEachStopWithALinkToItsScreen()
    {
        await using var app = new DriverAppWebFactory("Development", standIn: true);
        using var client = await SignedIn(app, DriverAppWebFactory.AnoukId);

        var page = await GetAsync(client, "/");

        page.Should().Contain(Collection).And.Contain(Delivery).And.Contain("/trips/TR-0000002/stops/SH-0000002/delivery");
    }

    [Fact]
    public async Task TheStopScreen_ShowsTheShipmentTheShipperTheAddressTheWindowAndTheGoods_AndForADeliveryTheConsignee()
    {
        await using var app = new DriverAppWebFactory("Development", standIn: true);
        using var client = await SignedIn(app, DriverAppWebFactory.AnoukId);

        var collection = WebUtility.HtmlDecode(await GetAsync(client, Collection));
        var delivery = WebUtility.HtmlDecode(await GetAsync(client, Delivery));

        collection.Should().Contain("SH-0000001").And.Contain("TO-0000001").And.Contain("SHP-001").And.Contain("Waalhaven 1, 3087 BM Rotterdam, NL").And.Contain("2 × Pallet").And.Contain("No window");
        collection.Should().NotContain("Bakker BV", "a collection has no Consignee");
        delivery.Should().Contain("Consignee").And.Contain("Bakker BV").And.Contain("010-1234567");
    }

    [Fact]
    public async Task BeforeTheTripIsInProgress_TheStopOffersNothingToCompleteAndSaysToStartTheTripFirst()
    {
        await using var app = new DriverAppWebFactory("Development", standIn: true);
        using var client = await SignedIn(app, DriverAppWebFactory.AnoukId);

        var page = await GetAsync(client, Collection);

        page.Should().Contain("Start the Trip first.").And.NotContain("/collected\"").And.NotContain("Report damage");
    }

    [Fact]
    public async Task ACollectionStop_OffersCollectedCollectionFailedAndReportDamage_AndADeliveryOnlyOnceItsCollectionIsDone()
    {
        await using var app = new DriverAppWebFactory("Development", standIn: true);
        app.StartAnouksTrip();
        using var client = await SignedIn(app, DriverAppWebFactory.AnoukId);

        var collection = await GetAsync(client, Collection);
        var delivery = await GetAsync(client, Delivery);

        collection.Should().Contain(Collection + "/collected").And.Contain(Collection + "/collection-failed").And.Contain(Collection + "/damaged");
        delivery.Should().Contain("Collect the Shipment first.").And.NotContain("/delivery-failed").And.Contain(Delivery + "/damaged", "damage can be reported on any Stop");
    }

    [Fact]
    public async Task PressingCollected_PublishesExactlyOneEvent_WithTheDriverIdFromTheToken_AndTheDeliveryBecomesAvailable()
    {
        await using var app = new DriverAppWebFactory("Development", standIn: true);
        app.StartAnouksTrip();
        using var client = await SignedIn(app, DriverAppWebFactory.AnoukId);

        var posted = await PostAsync(client, Collection, "collected", await GetAsync(client, Collection));

        posted.StatusCode.Should().Be(HttpStatusCode.Redirect);
        var published = app.Saved.Should().ContainSingle().Which.Should().BeOfType<StopCompletedReportedV1>().Subject;
        published.DriverId.Should().Be(DriverAppWebFactory.AnoukId);
        published.TripNumber.Should().Be("TR-0000002");
        published.ShipmentNumber.Should().Be("SH-0000001");
        published.TrackingEventType.Should().Be("Collected");
        published.Location.Should().Be(new StopLocationV1("Rotterdam", "3087 BM", "NL"));
        app.Reports.Single(report => report.Kind == ReportKind.Collected).State.Should().Be(ReportState.Sending);
        var delivery = await GetAsync(client, Delivery);
        delivery.Should().Contain(Delivery + "/delivery-failed").And.NotContain("Collect the Shipment first.");
        (await GetAsync(client, Collection)).Should().Contain("This Stop is done.").And.Contain("Sending").And.Contain("http-equiv=\"refresh\"");
        (await GetAsync(client, "/")).Should().Contain("Collected SH-0000001");
    }

    [Fact]
    public async Task APostThatNamesAnotherDriver_IsStillTheSignedInDriversReport()
    {
        await using var app = new DriverAppWebFactory("Development", standIn: true);
        app.StartAnouksTrip();
        using var client = await SignedIn(app, DriverAppWebFactory.AnoukId);
        var token = TokenOf(await GetAsync(client, Collection));

        using var post = await client.PostAsync(
            new Uri(Collection + "/collected", UriKind.Relative),
            new FormUrlEncodedContent([new("__RequestVerificationToken", token), new("driverId", DriverAppWebFactory.JoostId)]),
            Ct);

        post.StatusCode.Should().Be(HttpStatusCode.Redirect);
        app.Saved.Should().ContainSingle().Which.DriverId.Should().Be(DriverAppWebFactory.AnoukId);
    }

    [Fact]
    public async Task ADeliveryFailedWithARemark_IsPublishedWithTheRemark_OnceTheCollectionIsDone()
    {
        await using var app = new DriverAppWebFactory("Development", standIn: true);
        app.StartAnouksTrip();
        using var client = await SignedIn(app, DriverAppWebFactory.AnoukId);
        await PostAsync(client, Collection, "collected", await GetAsync(client, Collection));

        var posted = await PostAsync(client, Delivery, "delivery-failed", await GetAsync(client, Delivery), "Dock closed");

        posted.StatusCode.Should().Be(HttpStatusCode.Redirect);
        var published = app.Saved.Last().Should().BeOfType<StopCompletedReportedV1>().Subject;
        published.TrackingEventType.Should().Be("DeliveryFailed");
        published.Stop.Should().Be("Delivery");
        published.Remark.Should().Be("Dock closed");
    }

    [Fact]
    public async Task AFailureWithoutARemark_IsSentBackToTheStopScreenWithAMessage_AndPublishesNothing()
    {
        await using var app = new DriverAppWebFactory("Development", standIn: true);
        app.StartAnouksTrip();
        using var client = await SignedIn(app, DriverAppWebFactory.AnoukId);

        var posted = await PostAsync(client, Collection, "collection-failed", await GetAsync(client, Collection), "  ");

        posted.StatusCode.Should().Be(HttpStatusCode.Redirect);
        posted.Headers.Location!.OriginalString.Should().Be(Collection + "?problem=remark");
        app.Saved.Should().BeEmpty();
        (await GetAsync(client, posted.Headers.Location.OriginalString)).Should().Contain("a remark is required");
    }

    [Fact]
    public async Task ADeliveryPostedBeforeItsCollection_PublishesNothing_AndShowsTheStopScreen()
    {
        await using var app = new DriverAppWebFactory("Development", standIn: true);
        app.StartAnouksTrip();
        using var client = await SignedIn(app, DriverAppWebFactory.AnoukId);
        var token = await GetAsync(client, Collection);

        var posted = await PostAsync(client, Delivery, "delivery-failed", token, "Closed");

        posted.StatusCode.Should().Be(HttpStatusCode.Redirect);
        posted.Headers.Location!.OriginalString.Should().Be(Delivery);
        app.Saved.Should().BeEmpty();
    }

    [Fact]
    public async Task PressingReportDamage_PublishesDamaged_AndTheStopStaysOpen()
    {
        await using var app = new DriverAppWebFactory("Development", standIn: true);
        app.StartAnouksTrip();
        using var client = await SignedIn(app, DriverAppWebFactory.AnoukId);

        await PostAsync(client, Delivery, "damaged", await GetAsync(client, Delivery), "Crushed carton");

        app.Saved.Should().ContainSingle().Which.Should().BeOfType<StopCompletedReportedV1>().Which.TrackingEventType.Should().Be("Damaged");
        var page = await GetAsync(client, Delivery);
        page.Should().Contain("Damage reported").And.Contain("Crushed carton").And.Contain("Open");
    }

    [Theory]
    [InlineData("/trips/TR-0000003/stops/SH-0000001/collection")]
    [InlineData("/trips/TR-0000002/stops/SH-0000099/collection")]
    public async Task AStopOfAnotherDriverOrNotOnTheTrip_IsNotShown(string path)
    {
        await using var app = new DriverAppWebFactory("Development", standIn: true);
        using var client = await SignedIn(app, DriverAppWebFactory.AnoukId);

        var page = await GetAsync(client, path);

        page.Should().Contain("isn't on one of your Trips").And.NotContain("Waalhaven");
    }

    [Theory]
    [InlineData("/trips/TR-0000003/stops/SH-0000001/collection/collected")]
    [InlineData("/trips/TR-0000002/stops/SH-0000099/collection/collected")]
    [InlineData("/trips/TR-0000002/stops/SH-0000001/collection/vanished")]
    [InlineData("/trips/TR-0000002/stops/SH-0000001/pickup/collected")]
    public async Task ReportingOnAnotherDriversStopAStopNotOnTheTripOrWithAnUnknownAction_IsNotFound_AndPublishesNothing(string path)
    {
        await using var app = new DriverAppWebFactory("Development", standIn: true);
        app.StartAnouksTrip();
        using var client = await SignedIn(app, DriverAppWebFactory.AnoukId);
        var token = TokenOf(await GetAsync(client, Collection));

        using var posted = await client.PostAsync(new Uri(path, UriKind.Relative), new FormUrlEncodedContent([new("__RequestVerificationToken", token)]), Ct);

        posted.StatusCode.Should().Be(HttpStatusCode.NotFound);
        app.Saved.Should().BeEmpty();
    }

    [Fact]
    public async Task ReportingWithoutAnAntiforgeryTokenOrASignIn_IsRefused_AndPublishesNothing()
    {
        await using var app = new DriverAppWebFactory("Development", standIn: true);
        app.StartAnouksTrip();
        using var client = await SignedIn(app, DriverAppWebFactory.AnoukId);
        using var stranger = app.CreateClient(Browser);

        using var withoutToken = await client.PostAsync(new Uri(Collection + "/collected", UriKind.Relative), new FormUrlEncodedContent([]), Ct);
        using var withoutSignIn = await stranger.PostAsync(new Uri(Collection + "/collected", UriKind.Relative), new FormUrlEncodedContent([]), Ct);

        withoutToken.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        withoutSignIn.StatusCode.Should().BeOneOf(HttpStatusCode.Redirect, HttpStatusCode.BadRequest);
        app.Saved.Should().BeEmpty();
    }

    [Fact]
    public async Task ARefusedShipmentNotOnTrip_ShowsAsRefusedOnTheStop_AndAStopTakenOffInANewVersionIsKeptAndMarked()
    {
        await using var app = new DriverAppWebFactory("Development", standIn: true);
        app.StartAnouksTrip();
        using var client = await SignedIn(app, DriverAppWebFactory.AnoukId);
        await PostAsync(client, Collection, "collected", await GetAsync(client, Collection));
        app.Reports.Single(report => report.Kind == ReportKind.Collected)
            .Refuse("ShipmentNotOnTrip", "SH-0000001 is no longer on TR-0000002.", app.Time.GetUtcNow());

        var refused = await GetAsync(client, "/");
        var trip = app.Trips.Single(trip => trip.Id.Value == "TR-0000002");
        trip.ReplaceWith(2, trip.DriverId, trip.Details with { Stops = [.. trip.Details.Stops.Where(stop => stop.ShipmentNumber != "SH-0000001")] });
        var taken = await GetAsync(client, "/");
        var stopScreen = await GetAsync(client, Collection);

        refused.Should().Contain("Refused").And.Contain("ShipmentNotOnTrip").And.Contain("SH-0000001 is no longer on TR-0000002.");
        taken.Should().Contain("Taken off by the Planner, call the Planner").And.Contain("SH-0000001").And.Contain("Version 2");
        stopScreen.Should().Contain("Taken off by the Planner, call the Planner").And.NotContain("Waalhaven");
    }

    private static async Task<HttpClient> SignedIn(DriverAppWebFactory app, string driverId)
    {
        var client = app.CreateClient(Browser);
        using var signedIn = await client.PostAsync(new Uri("/local/sign-in", UriKind.Relative), new FormUrlEncodedContent([new("driver", driverId)]), Ct);
        signedIn.StatusCode.Should().Be(HttpStatusCode.Redirect);
        return client;
    }

    private static Task<string> GetAsync(HttpClient client, string path) => client.GetStringAsync(new Uri(path, UriKind.Relative), Ct);

    private static async Task<HttpResponseMessage> PostAsync(HttpClient client, string stop, string action, string page, string? remark = null)
    {
        List<KeyValuePair<string, string>> form = [new("__RequestVerificationToken", TokenOf(page))];
        if (remark is not null)
        {
            form.Add(new("remark", remark));
        }

        return await client.PostAsync(new Uri($"{stop}/{action}", UriKind.Relative), new FormUrlEncodedContent(form), Ct);
    }

    /// <summary>The antiforgery token the page's form carries, which the browser would post back.</summary>
    private static string TokenOf(string page) =>
        AntiforgeryToken().Match(page) is { Success: true } match
            ? WebUtility.HtmlDecode(match.Groups[1].Value)
            : throw new InvalidOperationException("The page has no form with an antiforgery token.");

    [GeneratedRegex("name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex AntiforgeryToken();
}
