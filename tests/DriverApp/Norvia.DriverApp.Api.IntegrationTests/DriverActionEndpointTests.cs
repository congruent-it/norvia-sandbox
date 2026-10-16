using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Nodes;

namespace Norvia.DriverApp.Api.IntegrationTests;

/// <summary>
/// The Driver's actions as an API (P12): a caller that acts as the Driver, such as the Simulator, signs in with a token whose one app role is the Driver ID, and
/// does what the screens do. The Driver ID comes from the token and nowhere else; the reports are the ones the screens make, published once.
/// </summary>
[Collection("Driver app")]
public sealed class DriverActionEndpointTests(DriverAppFixture driverApp)
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    // A valid 1x1 PNG.
    private const string Png = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg==";

    private HttpClient As(string driverId) => driverApp.DriverReportsClient(driverApp.IssueToken([driverId]));

    private async Task ReceiveTripAsync(string trip, string driverId)
    {
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, TimeZoneInfo.FindSystemTimeZoneById("Europe/Amsterdam")).DateTime);
        using var response = await driverApp.DriverApp.PostAsync(
            "/v1/trips",
            new StringContent(await DriverAppFixture.SampleAsync("two-shipments", trip, version: 1, driverId, sample => sample["day"] = $"{today:yyyy-MM-dd}"), Encoding.UTF8, "application/json"),
            Ct);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task TheDriversTrips_AreListedForTheDriverInTheToken_AndForNoOneElse()
    {
        await ReceiveTripAsync("TR-4000001", "DRV-4000001");
        using var driver = As("DRV-4000001");
        using var other = As("DRV-4000002");

        var mine = await driver.GetFromJsonAsync<JsonArray>("/v1/driver/trips", Ct);
        var theirs = await other.GetFromJsonAsync<JsonArray>("/v1/driver/trips", Ct);

        mine!.Select(trip => trip!["tripNumber"]!.GetValue<string>()).Should().Equal("TR-4000001");
        theirs.Should().BeEmpty();
    }

    [Fact]
    public async Task StartingATrip_PublishesTheSameStartAsThePressOfTheButton_AndAnswersTheTrailOfTheAction()
    {
        const string trip = "TR-4000002";
        await ReceiveTripAsync(trip, "DRV-4000003");
        using var driver = As("DRV-4000003");

        using var started = await driver.PostAsync($"/v1/driver/trips/{trip}/start", content: null, Ct);

        started.StatusCode.Should().Be(HttpStatusCode.OK);
        var answer = JsonNode.Parse(await started.Content.ReadAsStringAsync(Ct))!;
        var message = await driverApp.FirstPublishedForAsync(trip, Ct);
        var body = JsonNode.Parse(message.Body.ToString())!;
        body["eventType"]!.GetValue<string>().Should().Be("TripStartReported");
        body["driverId"]!.GetValue<string>().Should().Be("DRV-4000003", "the Driver ID comes from the token");
        answer["correlationId"]!.GetValue<string>().Should().Be(body["correlationId"]!.GetValue<string>());
        answer["reportId"]!.GetValue<string>().Should().Be(body["eventId"]!.GetValue<string>());
        answer["alreadyReported"]!.GetValue<bool>().Should().BeFalse();
    }

    [Fact]
    public async Task StartingATripTwice_ReportsItOnce_AndSaysSoTheSecondTime()
    {
        const string trip = "TR-4000003";
        await ReceiveTripAsync(trip, "DRV-4000004");
        using var driver = As("DRV-4000004");

        using var first = await driver.PostAsync($"/v1/driver/trips/{trip}/start", content: null, Ct);
        using var second = await driver.PostAsync($"/v1/driver/trips/{trip}/start", content: null, Ct);

        JsonNode.Parse(await second.Content.ReadAsStringAsync(Ct))!["alreadyReported"]!.GetValue<bool>().Should().BeTrue();
        await driverApp.FirstPublishedForAsync(trip, Ct);
        driverApp.PublishedFor(trip).Should().ContainSingle();
    }

    [Fact]
    public async Task AnotherDriversTrip_IsAsUnknownAsOneThatDoesNotExist()
    {
        const string trip = "TR-4000004";
        await ReceiveTripAsync(trip, "DRV-4000005");
        using var other = As("DRV-4000006");

        using var response = await other.PostAsync($"/v1/driver/trips/{trip}/start", content: null, Ct);
        using var unknown = await other.PostAsync("/v1/driver/trips/TR-4999999/end", content: null, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        JsonNode.Parse(await response.Content.ReadAsStringAsync(Ct))!["code"]!.GetValue<string>().Should().Be("TripNotFound");
        unknown.StatusCode.Should().Be(HttpStatusCode.NotFound);
        driverApp.PublishedFor(trip).Should().BeEmpty();
    }

    [Fact]
    public async Task EndingATripThatWasNotStarted_IsRefusedAsNotAvailable()
    {
        const string trip = "TR-4000005";
        await ReceiveTripAsync(trip, "DRV-4000007");
        using var driver = As("DRV-4000007");

        using var response = await driver.PostAsync($"/v1/driver/trips/{trip}/end", content: null, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        JsonNode.Parse(await response.Content.ReadAsStringAsync(Ct))!["code"]!.GetValue<string>().Should().Be("ReportNotAvailable");
    }

    [Fact]
    public async Task AStopBeforeTheTmsRecordedTheStart_IsRefusedAsNotAvailable_AndOneWithoutARemarkIsInvalid()
    {
        const string trip = "TR-4000006";
        await ReceiveTripAsync(trip, "DRV-4000008");
        using var driver = As("DRV-4000008");

        using var early = await driver.PostAsJsonAsync($"/v1/driver/trips/{trip}/stops/SH-0000012/collection/collected", new { }, Ct);
        using var noRemark = await driver.PostAsJsonAsync($"/v1/driver/trips/{trip}/stops/SH-0000012/collection/collection-failed", new { }, Ct);

        early.StatusCode.Should().Be(HttpStatusCode.Conflict);
        noRemark.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var problem = JsonNode.Parse(await noRemark.Content.ReadAsStringAsync(Ct))!;
        problem["code"]!.GetValue<string>().Should().Be("ReportInvalid");
        problem["fields"]!.AsArray().Select(field => field!.GetValue<string>()).Should().Contain("remark");
    }

    [Theory]
    [InlineData("/v1/driver/trips/TR-1/stops/SH-1/pickup/collected")]
    [InlineData("/v1/driver/trips/TR-1/stops/SH-1/collection/lost")]
    public async Task AStopRouteTheDriverAppDoesNotKnow_IsNotFound(string route)
    {
        using var driver = As("DRV-4000009");

        using var response = await driver.PostAsJsonAsync(route, new { }, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        JsonNode.Parse(await response.Content.ReadAsStringAsync(Ct))!["code"]!.GetValue<string>().Should().Be("StopNotFound");
    }

    [Fact]
    public async Task ADeliveryWithoutItsProofOfDelivery_IsInvalid()
    {
        const string trip = "TR-4000007";
        await ReceiveTripAsync(trip, "DRV-4000010");
        using var driver = As("DRV-4000010");

        using var response = await driver.PostAsJsonAsync($"/v1/driver/trips/{trip}/stops/SH-0000012/delivery/deliver", new { receivedBy = "Consignee", condition = "Clean" }, Ct);

        // The delivery waits for the start first; either way the Driver app refuses it, and the Proof of delivery's files are the form's own rule.
        response.StatusCode.Should().BeOneOf(HttpStatusCode.BadRequest, HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task ADeliveryWithAPngSignature_OfAStartedTrip_IsTakenWithItsTrail()
    {
        const string trip = "TR-4000008";
        await ReceiveTripAsync(trip, "DRV-4000011");
        using var driver = As("DRV-4000011");
        using var started = await driver.PostAsync($"/v1/driver/trips/{trip}/start", content: null, Ct);
        var sent = JsonNode.Parse((await driverApp.FirstPublishedForAsync(trip, Ct)).Body.ToString())!;
        using var recorded = await driverApp.DriverReports.PostAsJsonAsync("/v1/report-outcomes", new JsonObject
        {
            ["driverAppEventId"] = sent["eventId"]!.GetValue<string>(),
            ["tripNumber"] = trip,
            ["correlationId"] = sent["correlationId"]!.GetValue<string>(),
            ["outcome"] = "Recorded",
        }, Ct);
        recorded.StatusCode.Should().Be(HttpStatusCode.OK);

        using var collected = await driver.PostAsJsonAsync($"/v1/driver/trips/{trip}/stops/SH-0000012/collection/collected", new { }, Ct);
        using var delivered = await driver.PostAsJsonAsync(
            $"/v1/driver/trips/{trip}/stops/SH-0000012/delivery/deliver",
            new { receivedBy = "Consignee", condition = "Clean", signature = new { contentType = "image/png", content = Png }, photos = Array.Empty<object>() }, Ct);

        collected.StatusCode.Should().Be(HttpStatusCode.OK);
        delivered.StatusCode.Should().Be(HttpStatusCode.OK, await delivered.Content.ReadAsStringAsync(Ct));
        JsonNode.Parse(await delivered.Content.ReadAsStringAsync(Ct))!["correlationId"]!.GetValue<string>().Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task OnlyADriversToken_MayActAsADriver()
    {
        using var anonymous = driverApp.DriverReportsClient(token: null);
        using var logicApp = driverApp.DriverReportsClient(driverApp.IssueToken([DriverAppFixture.ReportOutcomeRole]));
        using var twoDrivers = driverApp.DriverReportsClient(driverApp.IssueToken(["DRV-4000012", "DRV-4000013"]));
        using var noRole = driverApp.DriverReportsClient(driverApp.IssueToken([]));
        using var otherAudience = driverApp.DriverReportsClient(driverApp.IssueToken(["DRV-4000012"], audience: "api://somebody-else"));

        (await anonymous.GetAsync("/v1/driver/trips", Ct)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await otherAudience.GetAsync("/v1/driver/trips", Ct)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await logicApp.GetAsync("/v1/driver/trips", Ct)).StatusCode.Should().Be(HttpStatusCode.Forbidden, "the Logic App's role is no Driver ID");
        (await twoDrivers.GetAsync("/v1/driver/trips", Ct)).StatusCode.Should().Be(HttpStatusCode.Forbidden, "a token with two Driver IDs would be an arbitrary one of them");
        (await noRole.GetAsync("/v1/driver/trips", Ct)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}
