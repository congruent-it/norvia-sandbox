using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using static Norvia.Tms.TestSupport.Capacity.CapacityCalls;

namespace Norvia.Tms.Api.IntegrationTests.Trips;

/// <summary>
/// The delivery state of a Trip version (P11) at the TMS's boundary: the Trip dispatch Logic App reports a version as delivered or not delivered
/// under the role Trips.ReportDelivery, Not delivered publishes TripNotDelivered, and the Planner can send a Not delivered version again, which
/// publishes TripDispatched with the same version under a new correlation ID. Each test moves the clock to a stretch of days of its own.
/// </summary>
public sealed class TripDeliveryTests(TripsTmsFixture tms) : IClassFixture<TripsTmsFixture>, IDisposable
{
    private const string Truck = "VEH-0002";
    private const string Joost = "DRV-0002";

    private static readonly TimeZoneInfo Amsterdam = TimeZoneInfo.FindSystemTimeZoneById("Europe/Amsterdam");

    private readonly CancellationToken _cancellationToken = TestContext.Current.CancellationToken;
    private readonly DateOnly _realToday = Today(DateTimeOffset.UtcNow);

    public void Dispose() => tms.ResetClock();

    [Fact]
    public async Task ADispatchedTripVersion_IsSending_AndShowsItOnTheTripsViewAndTheTripDetail()
    {
        var (trip, _) = await DispatchedTripAsync(5000);

        var detail = await GetTripDetailAsync(trip);

        detail["trip"]!["latestDelivery"]!.GetValue<string>().Should().Be("Sending");
        VersionsOf(detail).Should().Equal("1 Sending");
        (await ListedAsync(trip))["latestDelivery"]!.GetValue<string>().Should().Be("Sending");
    }

    [Fact]
    public async Task ADeliveredReport_SetsTheVersionDelivered_AndPublishesNothing()
    {
        var (trip, dispatchCorrelationId) = await DispatchedTripAsync(5100);

        using var reported = await ReportDeliveredAsync(trip, 1, dispatchCorrelationId);

        reported.StatusCode.Should().Be(HttpStatusCode.OK, await reported.Content.ReadAsStringAsync(_cancellationToken));
        var detail = await GetTripDetailAsync(trip);
        VersionsOf(detail).Should().Equal("1 Delivered");
        detail["trip"]!["latestDelivery"]!.GetValue<string>().Should().Be("Delivered");
        (await ListedAsync(trip))["latestDelivery"]!.GetValue<string>().Should().Be("Delivered");
        await TmsFixture.SettleOutboxAsync(_cancellationToken);
        tms.Published.Where(m => m.ApplicationProperties.TryGetValue("tripNumber", out var number) && number.Equals(trip)).Select(m => m.Subject).Should().Equal("TripDispatched");
    }

    [Fact]
    public async Task ANotDeliveredReport_SetsTheVersionNotDelivered_WithItsCause_AndPublishesTripNotDelivered()
    {
        var (trip, dispatchCorrelationId) = await DispatchedTripAsync(5200);

        using var reported = await ReportNotDeliveredAsync(trip, 1, dispatchCorrelationId, "The Driver app answered 503 after 4 attempts.");

        reported.StatusCode.Should().Be(HttpStatusCode.OK, await reported.Content.ReadAsStringAsync(_cancellationToken));
        var detail = await GetTripDetailAsync(trip);
        VersionsOf(detail).Should().Equal("1 NotDelivered");
        detail["versions"]![0]!["cause"]!.GetValue<string>().Should().Be("The Driver app answered 503 after 4 attempts.");
        detail["trip"]!["latestDelivery"]!.GetValue<string>().Should().Be("NotDelivered");

        var message = (await tms.PublishedAsync(dispatchCorrelationId, "TripNotDelivered", _cancellationToken)).Should().ContainSingle().Subject;
        message.ApplicationProperties["tripNumber"].Should().Be(trip);
        message.ApplicationProperties["flow"].Should().Be("trip-dispatch");
        var body = JsonNode.Parse(message.Body.ToString())!;
        var results = CanonicalModelSchemas.ValidateTripNotDelivered(body);
        results.IsValid.Should().BeTrue(CanonicalModelSchemas.Errors(results));
        body["tripNumber"]!.GetValue<string>().Should().Be(trip);
        body["version"]!.GetValue<int>().Should().Be(1);
        body["driverId"]!.GetValue<string>().Should().Be(Joost);
        body["cause"]!.GetValue<string>().Should().Be("The Driver app answered 503 after 4 attempts.");
        body["correlationId"]!.GetValue<string>().Should().Be(dispatchCorrelationId);
    }

    [Fact]
    public async Task ARepeatOrLateReport_IsIgnored_AndPublishesNothingMore()
    {
        var (trip, dispatchCorrelationId) = await DispatchedTripAsync(5300);
        (await ReportNotDeliveredAsync(trip, 1, dispatchCorrelationId, "Down")).StatusCode.Should().Be(HttpStatusCode.OK);
        await tms.PublishedAsync(dispatchCorrelationId, "TripNotDelivered", _cancellationToken);

        using var repeat = await ReportNotDeliveredAsync(trip, 1, dispatchCorrelationId, "Down again");
        using var late = await ReportDeliveredAsync(trip, 1, dispatchCorrelationId);

        repeat.StatusCode.Should().Be(HttpStatusCode.OK);
        JsonNode.Parse(await repeat.Content.ReadAsStringAsync(_cancellationToken))!["ignored"]!.GetValue<bool>().Should().BeTrue();
        JsonNode.Parse(await late.Content.ReadAsStringAsync(_cancellationToken))!["ignored"]!.GetValue<bool>().Should().BeTrue();
        VersionsOf(await GetTripDetailAsync(trip)).Should().Equal("1 NotDelivered");
        await TmsFixture.SettleOutboxAsync(_cancellationToken);
        tms.Published.Count(m => m.Subject == "TripNotDelivered" && m.ApplicationProperties.TryGetValue("tripNumber", out var number) && number.Equals(trip)).Should().Be(1);
    }

    [Fact]
    public async Task AReportForAVersionTheTripNeverHad_AnUnknownTrip_OrWithoutTheCauseOrTheCorrelationId_IsRefused()
    {
        var (trip, dispatchCorrelationId) = await DispatchedTripAsync(5400);

        await AssertRefusedAsync(await ReportDeliveredAsync(trip, 2, dispatchCorrelationId), HttpStatusCode.NotFound, "TripVersionNotFound");
        await AssertRefusedAsync(await ReportDeliveredAsync("TR-9999999", 1, dispatchCorrelationId), HttpStatusCode.NotFound, "TripNotFound");
        (await ReportNotDeliveredAsync(trip, 1, dispatchCorrelationId, cause: null)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ReportDeliveredAsync(trip, 1, correlationId: null)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        VersionsOf(await GetTripDetailAsync(trip)).Should().Equal("1 Sending");
    }

    [Fact]
    public async Task CallersWithoutTripsReportDelivery_AreRefused_AndNothingChanges()
    {
        var (trip, dispatchCorrelationId) = await DispatchedTripAsync(5500);

        foreach (var caller in new[] { tms.TmsUi, tms.Tms, tms.ShipperNotificationReport })
        {
            using var delivered = await ReportDeliveredAsync(trip, 1, dispatchCorrelationId, caller);
            using var notDelivered = await ReportNotDeliveredAsync(trip, 1, dispatchCorrelationId, "Down", caller);
            delivered.StatusCode.Should().Be(HttpStatusCode.Forbidden);
            notDelivered.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        }

        using var anonymous = tms.CreateTmsClient(token: null);
        (await ReportDeliveredAsync(trip, 1, dispatchCorrelationId, anonymous)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        VersionsOf(await GetTripDetailAsync(trip)).Should().Equal("1 Sending");
    }

    [Fact]
    public async Task OnlyThePlanner_CanSendAgain_NotTheLogicApp()
    {
        var (trip, dispatchCorrelationId) = await DispatchedTripAsync(5600);
        await ReportNotDeliveredAsync(trip, 1, dispatchCorrelationId, "Down");

        using var refused = await SendAgainAsync(trip, 1, tms.TripDeliveryReport);

        refused.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        VersionsOf(await GetTripDetailAsync(trip)).Should().Equal("1 NotDelivered");
    }

    [Fact]
    public async Task SendingANotDeliveredVersionAgain_RepublishesTripDispatchedWithTheSameVersionAndANewCorrelationId_AndTheVersionIsSending()
    {
        var (trip, dispatchCorrelationId) = await DispatchedTripAsync(5700);
        await ReportNotDeliveredAsync(trip, 1, dispatchCorrelationId, "Down");

        using var sent = await SendAgainAsync(trip, 1);

        var answer = JsonNode.Parse(await sent.Content.ReadAsStringAsync(_cancellationToken))!;
        sent.StatusCode.Should().Be(HttpStatusCode.OK, answer.ToJsonString());
        answer["version"]!.GetValue<int>().Should().Be(1);
        var again = answer["correlationId"]!.GetValue<string>();
        again.Should().NotBe(dispatchCorrelationId);
        VersionsOf(await GetTripDetailAsync(trip)).Should().Equal("1 Sending");

        var message = (await tms.PublishedAsync(again, "TripDispatched", _cancellationToken)).Should().ContainSingle().Subject;
        var body = JsonNode.Parse(message.Body.ToString())!;
        var results = CanonicalModelSchemas.ValidateTripDispatched(body);
        results.IsValid.Should().BeTrue(CanonicalModelSchemas.Errors(results));
        body["version"]!.GetValue<int>().Should().Be(1);
        body["tripNumber"]!.GetValue<string>().Should().Be(trip);
        body["stops"]!.AsArray().Should().HaveCount(2);

        // The new send gets its own report, which the Logic App sends on the new correlation ID.
        (await ReportDeliveredAsync(trip, 1, again)).StatusCode.Should().Be(HttpStatusCode.OK);
        VersionsOf(await GetTripDetailAsync(trip)).Should().Equal("1 Delivered");
    }

    [Fact]
    public async Task SendingAgainAVersionThatIsSendingDeliveredOrUnknown_OrADraft_IsRefused_AndPublishesNothing()
    {
        var (trip, dispatchCorrelationId) = await DispatchedTripAsync(5800);

        await AssertRefusedAsync(await SendAgainAsync(trip, 1), HttpStatusCode.Conflict, "NothingToSendAgain");
        await ReportDeliveredAsync(trip, 1, dispatchCorrelationId);
        await AssertRefusedAsync(await SendAgainAsync(trip, 1), HttpStatusCode.Conflict, "NothingToSendAgain");
        await AssertRefusedAsync(await SendAgainAsync(trip, 2), HttpStatusCode.NotFound, "TripVersionNotFound");
        await AssertRefusedAsync(await SendAgainAsync("TR-9999999", 1), HttpStatusCode.NotFound, "TripNotFound");

        var day = (await OpenHorizonAsync(5900)).AddDays(1);
        var draft = await NumberOf(await CreateAsync(day, Truck, Joost, "09:00", "04:00:00"));
        await AssertRefusedAsync(await SendAgainAsync(draft, 1), HttpStatusCode.Conflict, "TripNotDispatched");
        await TmsFixture.SettleOutboxAsync(_cancellationToken);
        tms.Published.Count(m => m.Subject == "TripDispatched" && m.ApplicationProperties.TryGetValue("tripNumber", out var number) && number.Equals(trip)).Should().Be(1);
    }

    private async Task<(string Trip, string CorrelationId)> DispatchedTripAsync(int daysAhead)
    {
        var day = (await OpenHorizonAsync(daysAhead)).AddDays(1);
        var shipment = await ReleasedAsync(day);
        var trip = await NumberOf(await CreateAsync(day, Truck, Joost, "09:00", "04:00:00", shipment));
        using var dispatched = await tms.TmsUi.PostAsync(new Uri($"/v1/trips/{trip}/dispatch", UriKind.Relative), content: null, _cancellationToken);
        var body = await dispatched.Content.ReadAsStringAsync(_cancellationToken);
        dispatched.StatusCode.Should().Be(HttpStatusCode.OK, body);
        var correlationId = JsonNode.Parse(body)!["correlationId"]!.GetValue<string>();
        await tms.PublishedAsync(correlationId, "TripDispatched", _cancellationToken);
        return (trip, correlationId);
    }

    private Task<HttpResponseMessage> ReportDeliveredAsync(string trip, int version, string? correlationId, HttpClient? caller = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, new Uri($"/v1/trips/{trip}/versions/{version}/report-delivered", UriKind.Relative));
        if (correlationId is not null)
        {
            request.Headers.Add("X-Correlation-ID", correlationId);
        }

        return (caller ?? tms.TripDeliveryReport).SendAsync(request, _cancellationToken);
    }

    private Task<HttpResponseMessage> ReportNotDeliveredAsync(string trip, int version, string? correlationId, string? cause, HttpClient? caller = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, new Uri($"/v1/trips/{trip}/versions/{version}/report-not-delivered", UriKind.Relative))
        {
            Content = JsonContent.Create(new JsonObject { ["cause"] = cause }),
        };
        if (correlationId is not null)
        {
            request.Headers.Add("X-Correlation-ID", correlationId);
        }

        return (caller ?? tms.TripDeliveryReport).SendAsync(request, _cancellationToken);
    }

    private Task<HttpResponseMessage> SendAgainAsync(string trip, int version, HttpClient? caller = null) =>
        (caller ?? tms.TmsUi).PostAsync(new Uri($"/v1/trips/{trip}/versions/{version}/send-again", UriKind.Relative), content: null, _cancellationToken);

    private async Task<JsonNode> GetTripDetailAsync(string number)
    {
        using var response = await tms.TmsUi.GetAsync(new Uri($"/v1/trips/{number}", UriKind.Relative), _cancellationToken);
        var body = await response.Content.ReadAsStringAsync(_cancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK, body);
        return JsonNode.Parse(body)!;
    }

    private async Task<JsonNode> ListedAsync(string number)
    {
        using var response = await tms.TmsUi.GetAsync(new Uri("/v1/trips", UriKind.Relative), _cancellationToken);
        var body = await response.Content.ReadAsStringAsync(_cancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK, body);
        return JsonNode.Parse(body)!.AsArray().Single(trip => trip!["number"]!.GetValue<string>() == number)!;
    }

    private static IEnumerable<string> VersionsOf(JsonNode detail) =>
        detail["versions"]!.AsArray().Select(version => $"{version!["version"]} {version["state"]}");

    private async Task<DateOnly> OpenHorizonAsync(int daysAhead)
    {
        var first = _realToday.AddDays(daysAhead);
        first = first.AddDays(((int)DayOfWeek.Monday - (int)first.DayOfWeek + 7) % 7);
        tms.SetClock(ShortlyAfterMidnightOf(first));
        await tms.HorizonStartingAtAsync(first, _cancellationToken);
        return first;
    }

    // A Released Own fleet Shipment that collects from the hour on the day, 2 pallets of 500 kg.
    private async Task<string> ReleasedAsync(DateOnly day, int collectFrom = 8)
    {
        var from = new DateTimeOffset(day.ToDateTime(new TimeOnly(collectFrom, 0)), Amsterdam.GetUtcOffset(day.ToDateTime(new TimeOnly(collectFrom, 0))));
        var booking = Bookings.Valid(TmsCalls.NewReference()).CollectedFrom(from);
        booking["goodsLines"] = new JsonArray { new JsonObject { ["packageType"] = "pallet", ["quantity"] = 2, ["weightKg"] = 500m, ["dimensionsCm"] = null } };
        var number = await tms.SubmitAcceptedAsync(booking, TmsCalls.NewCorrelationId(), _cancellationToken);
        await tms.WaitForRatingResultAsync(number, "Rated", _cancellationToken);
        var assigned = await tms.AssignPlannedAsync(number, PlanningCalls.ToOwnFleet(), _cancellationToken);
        var shipment = assigned["shipmentNumber"]!.GetValue<string>();
        await tms.ReleasedAsync(shipment, _cancellationToken);
        return shipment;
    }

    private async Task<HttpResponseMessage> CreateAsync(DateOnly day, string? vehicleId, string? driverId, string? start, string? duration, params string[] shipments)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri("/v1/trips", UriKind.Relative))
        {
            Content = JsonContent.Create(new
            {
                day = $"{day:yyyy-MM-dd}",
                vehicleId,
                driverId,
                plannedStart = start,
                plannedDrivingDuration = duration,
                shipmentNumbers = shipments.Length == 0 ? null : shipments,
            }),
        };
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
        return await tms.TmsUi.SendAsync(request, _cancellationToken);
    }

    private async Task<string> NumberOf(HttpResponseMessage created)
    {
        var body = await created.Content.ReadAsStringAsync(_cancellationToken);
        created.StatusCode.Should().Be(HttpStatusCode.Created, body);
        return JsonNode.Parse(body)!["number"]!.GetValue<string>();
    }

    private async Task AssertRefusedAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        using var _ = response;
        var body = await response.Content.ReadAsStringAsync(_cancellationToken);
        response.StatusCode.Should().Be(status, body);
        var problem = JsonNode.Parse(body)!.AsObject();
        problem["code"]!.GetValue<string>().Should().Be(code);
        problem["detail"]!.GetValue<string>().Should().NotBeNullOrWhiteSpace("the refusal has a readable message");
    }
}
