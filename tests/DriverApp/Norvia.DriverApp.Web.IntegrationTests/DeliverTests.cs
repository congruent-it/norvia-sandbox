using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;
using Norvia.DriverApp.Application.Events;
using Norvia.DriverApp.Domain.Reports;

namespace Norvia.DriverApp.Web.IntegrationTests;

/// <summary>
/// The Driver delivers a Stop with a Proof of delivery in the Driver app (P12): the form posts, with its files, publish exactly two events that share one
/// correlation ID, carrying the Driver ID from the token, and store the files in the transit container. A delivery waits for its collection, and a form that breaks
/// the rules publishes and stores nothing. Signed in with the local stand-in, with the clock on Tuesday 13 October 2026 and Anouk's Trip TR-0000002 on its day.
/// </summary>
public sealed partial class DeliverTests
{
    private const string Delivery = "/trips/TR-0000002/stops/SH-0000001/delivery";

    private static readonly WebApplicationFactoryClientOptions Browser = new() { AllowAutoRedirect = false, HandleCookies = true };
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;
    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3];
    private static readonly byte[] Jpeg = [0xFF, 0xD8, 0xFF, 0xE0, 4, 5, 6];

    private static string SignatureField => "data:image/png;base64," + Convert.ToBase64String(Png);

    [Fact]
    public async Task ADeliveryAfterItsCollection_ShowsTheProofOfDeliveryForm_WithTheSignatureTheConditionAndThePhotos()
    {
        await using var app = StartedWithCollections();
        using var client = await SignedIn(app);

        var page = WebUtility.HtmlDecode(await GetAsync(client, Delivery));

        page.Should().Contain($"action=\"{Delivery}/deliver\"").And.Contain("enctype=\"multipart/form-data\"").And.Contain("name=\"receivedBy\"").And.Contain("name=\"condition\"")
            .And.Contain("name=\"remark\"").And.Contain("<canvas").And.Contain("name=\"signature\"").And.Contain("name=\"photos\"").And.Contain("accept=\"image/jpeg,image/png\"");
        page.Should().Contain(">Deliver<").And.Contain(Delivery + "/delivery-failed");
    }

    [Fact]
    public async Task ADeliveryBeforeItsCollection_OffersNoProofOfDeliveryForm()
    {
        await using var app = new DriverAppWebFactory("Development", standIn: true);
        app.StartAnouksTrip();
        using var client = await SignedIn(app);

        var page = await GetAsync(client, Delivery);

        page.Should().Contain("Collect the Shipment first.").And.NotContain("name=\"receivedBy\"");
    }

    [Fact]
    public async Task Delivering_PublishesTwoEventsWithOneCorrelationId_WithTheDriverIdFromTheToken_AndStoresTheFilesInTheTransitContainer()
    {
        await using var app = StartedWithCollections();
        using var client = await SignedIn(app);

        using var posted = await DeliverAsync(client, await GetAsync(client, Delivery), photos: 2);

        posted.StatusCode.Should().Be(HttpStatusCode.Redirect);
        posted.Headers.Location!.OriginalString.Should().Be("/");
        app.Saved.Should().HaveCount(2);
        var stop = app.Saved[0].Should().BeOfType<StopCompletedReportedV1>().Subject;
        var captured = app.Saved[1].Should().BeOfType<ProofOfDeliveryCapturedV1>().Subject;
        stop.TrackingEventType.Should().Be("Delivered");
        stop.DriverId.Should().Be(DriverAppWebFactory.AnoukId);
        captured.DriverId.Should().Be(DriverAppWebFactory.AnoukId);
        captured.CorrelationId.Should().Be(stop.CorrelationId);
        captured.ReceivedBy.Should().Be("J. Bakker");
        captured.Condition.Should().Be("Clean");
        captured.Files.Select(file => file.Name).Should().Equal("signature.png", "photo-1.jpg", "photo-2.jpg");
        app.TransitFiles.Keys.Should().BeEquivalentTo(captured.Files.Select(file => file.BlobReference));
        app.TransitFiles[captured.Files[0].BlobReference].Content.Should().Equal(Png);
        app.TransitFiles[captured.Files[1].BlobReference].ContentType.Should().Be("image/jpeg");
        app.Reports.Select(report => report.Kind).Should().Contain([ReportKind.Delivered, ReportKind.ProofOfDelivery]);
    }

    [Fact]
    public async Task APostThatNamesAnotherDriver_IsStillTheSignedInDriversDelivery()
    {
        await using var app = StartedWithCollections();
        using var client = await SignedIn(app);

        using var posted = await DeliverAsync(client, await GetAsync(client, Delivery), extra: [new("driverId", "DRV-0002")]);

        posted.StatusCode.Should().Be(HttpStatusCode.Redirect);
        app.Saved.Should().OnlyContain(published => published.DriverId == DriverAppWebFactory.AnoukId);
    }

    [Fact]
    public async Task WithReservations_TheRemarkTravelsOnTheProofOfDelivery()
    {
        await using var app = StartedWithCollections();
        using var client = await SignedIn(app);

        using var posted = await DeliverAsync(client, await GetAsync(client, Delivery), condition: "reservations", remark: "Two cartons crushed");

        posted.StatusCode.Should().Be(HttpStatusCode.Redirect);
        var captured = app.Saved.OfType<ProofOfDeliveryCapturedV1>().Single();
        captured.Condition.Should().Be("Reservations");
        captured.Remark.Should().Be("Two cartons crushed");
    }

    [Theory]
    [InlineData("", "clean", null, true, "receivedBy")]
    [InlineData("J. Bakker", "", null, true, "condition")]
    [InlineData("J. Bakker", "reservations", " ", true, "remark")]
    [InlineData("J. Bakker", "clean", null, false, "signature")]
    public async Task AFormThatBreaksTheRules_ComesBackToTheStopWithTheProblem_AndPublishesAndStoresNothing(string receivedBy, string condition, string? remark, bool drawn, string problem)
    {
        await using var app = StartedWithCollections();
        using var client = await SignedIn(app);

        using var posted = await DeliverAsync(client, await GetAsync(client, Delivery), receivedBy, condition, remark, drawn);

        posted.StatusCode.Should().Be(HttpStatusCode.Redirect);
        posted.Headers.Location!.OriginalString.Should().Be($"{Delivery}?problem={problem}");
        app.Saved.Should().BeEmpty();
        app.TransitFiles.Should().BeEmpty();
        WebUtility.HtmlDecode(await GetAsync(client, posted.Headers.Location.OriginalString)).Should().Contain("name=\"receivedBy\"");
    }

    [Fact]
    public async Task FivePhotos_AreOneTooMany()
    {
        await using var app = StartedWithCollections();
        using var client = await SignedIn(app);

        using var posted = await DeliverAsync(client, await GetAsync(client, Delivery), photos: 5);

        posted.Headers.Location!.OriginalString.Should().Be($"{Delivery}?problem=photos");
        app.TransitFiles.Should().BeEmpty();
    }

    [Fact]
    public async Task APhotoOverTenMegabytes_IsNotTaken()
    {
        await using var app = StartedWithCollections();
        using var client = await SignedIn(app);

        using var posted = await DeliverAsync(client, await GetAsync(client, Delivery), bigPhoto: true);

        posted.Headers.Location!.OriginalString.Should().Be($"{Delivery}?problem=photos");
        app.Saved.Should().BeEmpty();
    }

    [Fact]
    public async Task ADeliveryBeforeItsCollection_IsRefusedWithoutStoringAnything()
    {
        await using var app = new DriverAppWebFactory("Development", standIn: true);
        app.StartAnouksTrip();
        using var client = await SignedIn(app);

        // The blocked delivery's screen has no Proof of delivery form, but its damage form carries the token a stale page would still post.
        using var posted = await DeliverAsync(client, await GetAsync(client, Delivery));

        posted.StatusCode.Should().Be(HttpStatusCode.Redirect);
        posted.Headers.Location!.OriginalString.Should().Be(Delivery);
        app.Saved.Should().BeEmpty();
        app.TransitFiles.Should().BeEmpty();
    }

    [Fact]
    public async Task APostForAStopThatIsNotTheDrivers_IsNotFound()
    {
        await using var app = StartedWithCollections();
        using var client = await SignedIn(app);

        using var posted = await DeliverAsync(client, await GetAsync(client, Delivery), path: "/trips/TR-0000003/stops/SH-0000001/delivery/deliver");

        posted.StatusCode.Should().Be(HttpStatusCode.NotFound);
        app.Saved.Should().BeEmpty();
    }

    [Fact]
    public async Task DeliveringWithoutAnAntiforgeryTokenOrASignIn_IsRefused_AndPublishesNothing()
    {
        await using var app = StartedWithCollections();
        using var client = await SignedIn(app);
        using var stranger = app.CreateClient(Browser);

        using var withoutToken = await client.PostAsync(new Uri(Delivery + "/deliver", UriKind.Relative), new MultipartFormDataContent(), Ct);
        using var withoutSignIn = await stranger.PostAsync(new Uri(Delivery + "/deliver", UriKind.Relative), new MultipartFormDataContent(), Ct);

        withoutToken.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        withoutSignIn.StatusCode.Should().NotBe(HttpStatusCode.OK);
        app.Saved.Should().BeEmpty();
    }

    [Fact]
    public async Task OnceDelivered_TheStopIsDone_AndTheProofOfDeliveryShowsAsAReportOnIt()
    {
        await using var app = StartedWithCollections();
        using var client = await SignedIn(app);
        (await DeliverAsync(client, await GetAsync(client, Delivery))).StatusCode.Should().Be(HttpStatusCode.Redirect);

        var page = await GetAsync(client, Delivery);

        page.Should().Contain("This Stop is done.").And.Contain("Delivered").And.Contain("Proof of delivery").And.NotContain("name=\"receivedBy\"");
    }

    private static DriverAppWebFactory StartedWithCollections()
    {
        var app = new DriverAppWebFactory("Development", standIn: true);
        app.StartAnouksTrip();
        app.CollectAnouksShipments();
        return app;
    }

    private static async Task<HttpClient> SignedIn(DriverAppWebFactory app)
    {
        var client = app.CreateClient(Browser);
        using var signedIn = await client.PostAsync(new Uri("/local/sign-in", UriKind.Relative), new FormUrlEncodedContent([new("driver", DriverAppWebFactory.AnoukId)]), Ct);
        signedIn.StatusCode.Should().Be(HttpStatusCode.Redirect);
        return client;
    }

    private static Task<string> GetAsync(HttpClient client, string path) => client.GetStringAsync(new Uri(path, UriKind.Relative), Ct);

    private static async Task<HttpResponseMessage> DeliverAsync(
        HttpClient client, string page, string receivedBy = "J. Bakker", string condition = "clean", string? remark = null, bool drawn = true, int photos = 0, bool bigPhoto = false,
        string? path = null, List<KeyValuePair<string, string>>? extra = null)
    {
        using var form = new MultipartFormDataContent
        {
            { new StringContent(TokenOf(page)), "__RequestVerificationToken" },
            { new StringContent(receivedBy), "receivedBy" },
            { new StringContent(condition), "condition" },
            { new StringContent(remark ?? string.Empty), "remark" },
            { new StringContent(drawn ? SignatureField : string.Empty), "signature" },
        };
        foreach (var (name, value) in extra ?? [])
        {
            form.Add(new StringContent(value), name);
        }

        for (var i = 0; i < photos; i++)
        {
            form.Add(Photo(Jpeg, "image/jpeg", $"camera-{i}.jpg"), "photos", $"camera-{i}.jpg");
        }

        if (bigPhoto)
        {
            form.Add(Photo(new byte[(10 * 1024 * 1024) + 1], "image/jpeg", "big.jpg"), "photos", "big.jpg");
        }

        // The photos field is posted even when the Driver took none, as a browser does: an empty file.
        var none = Photo([], "application/octet-stream", string.Empty);
        none.Headers.ContentDisposition = new("form-data") { Name = "\"photos\"", FileName = "\"\"" };
        form.Add(none);
        return await client.PostAsync(new Uri(path ?? Delivery + "/deliver", UriKind.Relative), form, Ct);
    }

    private static ByteArrayContent Photo(byte[] bytes, string contentType, string _)
    {
        var content = new ByteArrayContent(bytes);
        content.Headers.ContentType = new(contentType);
        return content;
    }

    private static string TokenOf(string page) =>
        AntiforgeryToken().Match(page) is { Success: true } match
            ? WebUtility.HtmlDecode(match.Groups[1].Value)
            : throw new InvalidOperationException("The page has no form with an antiforgery token.");

    [GeneratedRegex("name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex AntiforgeryToken();
}
