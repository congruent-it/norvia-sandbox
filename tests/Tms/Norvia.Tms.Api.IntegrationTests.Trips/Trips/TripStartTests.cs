using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using static Norvia.Tms.TestSupport.Capacity.CapacityCalls;

namespace Norvia.Tms.Api.IntegrationTests.Trips;

/// <summary>
/// Starting a Dispatched Trip (P12) at the TMS's boundary: the Driver reports Start under the role Trips.Start, a Dispatched Trip on its day is
/// In progress and publishes exactly one TripStarted under the Driver's correlation ID, the rules refuse the rest, and from then on every Amend
/// but taking off an uncollected Shipment is TripUnderWay. The clock is controlled, so each test sets the day it starts on.
/// </summary>
public sealed class TripStartTests(TripsTmsFixture tms) : IClassFixture<TripsTmsFixture>, IDisposable
{
    private const string Truck = "VEH-0002";
    private const string Joost = "DRV-0002";
    private const string Anouk = "DRV-0001";

    private static readonly TimeZoneInfo Amsterdam = TimeZoneInfo.FindSystemTimeZoneById("Europe/Amsterdam");

    private readonly CancellationToken _cancellationToken = TestContext.Current.CancellationToken;
    private readonly DateOnly _realToday = Today(DateTimeOffset.UtcNow);

    public void Dispose() => tms.ResetClock();

    [Fact]
    public async Task StartingADispatchedTripOnItsDay_MakesItInProgress_ShowsItOnTheTripsView_AndPublishesExactlyOneTripStarted()
    {
        var (trip, day, _) = await DispatchedTripAsync(6000);
        tms.SetClock(ShortlyAfterMidnightOf(day));
        var correlationId = TmsCalls.NewCorrelationId();

        using var started = await StartAsync(trip, Joost, correlationId);

        var answer = JsonNode.Parse(await started.Content.ReadAsStringAsync(_cancellationToken))!;
        started.StatusCode.Should().Be(HttpStatusCode.OK, answer.ToJsonString());
        answer["version"]!.GetValue<int>().Should().Be(1);
        (await GetTripAsync(trip))["trip"]!["status"]!.GetValue<string>().Should().Be("InProgress");
        (await ListedAsync(trip))["status"]!.GetValue<string>().Should().Be("InProgress");

        var message = (await tms.PublishedAsync(correlationId, "TripStarted", _cancellationToken)).Should().ContainSingle().Subject;
        message.ApplicationProperties["tripNumber"].Should().Be(trip);
        message.ApplicationProperties["flow"].Should().Be("trip-execution");
        var body = JsonNode.Parse(message.Body.ToString())!;
        var results = CanonicalModelSchemas.ValidateTripStarted(body);
        results.IsValid.Should().BeTrue(CanonicalModelSchemas.Errors(results));
        body["tripNumber"]!.GetValue<string>().Should().Be(trip);
        body["version"]!.GetValue<int>().Should().Be(1);
        body["driverId"]!.GetValue<string>().Should().Be(Joost);
        body["correlationId"]!.GetValue<string>().Should().Be(correlationId);
        await TmsFixture.SettleOutboxAsync(_cancellationToken);
        tms.Published.Count(m => m.Subject == "TripStarted" && m.ApplicationProperties.TryGetValue("tripNumber", out var number) && number.Equals(trip)).Should().Be(1);
    }

    [Fact]
    public async Task ARepeatedDriverAppEventId_IsAnsweredAsARepeat_AndPublishesNothingMore()
    {
        var (trip, day, _) = await DispatchedTripAsync(6050);
        tms.SetClock(ShortlyAfterMidnightOf(day));
        var correlationId = TmsCalls.NewCorrelationId();
        var eventId = Guid.NewGuid().ToString();

        using var first = await StartAsync(trip, Joost, correlationId, driverAppEventId: eventId);
        using var again = await StartAsync(trip, Joost, correlationId, driverAppEventId: eventId);

        first.StatusCode.Should().Be(HttpStatusCode.OK);
        JsonNode.Parse(await first.Content.ReadAsStringAsync(_cancellationToken))!["repeat"]!.GetValue<bool>().Should().BeFalse();
        var answer = JsonNode.Parse(await again.Content.ReadAsStringAsync(_cancellationToken))!;
        again.StatusCode.Should().Be(HttpStatusCode.OK, answer.ToJsonString());
        answer["repeat"]!.GetValue<bool>().Should().BeTrue();
        answer["correlationId"]!.GetValue<string>().Should().Be(correlationId);
        await TmsFixture.SettleOutboxAsync(_cancellationToken);
        tms.Published.Count(m => m.Subject == "TripStarted" && m.ApplicationProperties.TryGetValue("tripNumber", out var number) && number.Equals(trip)).Should().Be(1);

        // Another event for a Trip that is under way is still a refused Start, and another Driver's Trip is not theirs to repeat.
        await AssertRefusedAsync(await StartAsync(trip, Joost, driverAppEventId: Guid.NewGuid().ToString()), HttpStatusCode.Conflict, "TripNotDispatched");
        await AssertRefusedAsync(await StartAsync(trip, Anouk, correlationId, driverAppEventId: eventId), HttpStatusCode.NotFound, "TripNotFound");
    }

    [Fact]
    public async Task AnotherDay_IsNotTripDay()
    {
        var (trip, day, _) = await DispatchedTripAsync(6100);

        // The clock is on the day before the Trip's day, and then on the day after.
        await AssertRefusedAsync(await StartAsync(trip, Joost), HttpStatusCode.Conflict, "NotTripDay");
        tms.SetClock(ShortlyAfterMidnightOf(day.AddDays(1)));
        await AssertRefusedAsync(await StartAsync(trip, Joost), HttpStatusCode.Conflict, "NotTripDay");
        (await GetTripAsync(trip))["trip"]!["status"]!.GetValue<string>().Should().Be("Dispatched");
    }

    [Fact]
    public async Task AnotherDriver_AndAnUnknownTrip_AreTripNotFound_AndADraftOrAStartedTrip_IsTripNotDispatched()
    {
        var (trip, day, _) = await DispatchedTripAsync(6200);
        var draft = await NumberOf(await CreateAsync(day.AddDays(1), Truck, Anouk, "09:00", "04:00:00"));
        tms.SetClock(ShortlyAfterMidnightOf(day));

        await AssertRefusedAsync(await StartAsync(trip, Anouk), HttpStatusCode.NotFound, "TripNotFound");
        await AssertRefusedAsync(await StartAsync("TR-9999999", Joost), HttpStatusCode.NotFound, "TripNotFound");
        tms.SetClock(ShortlyAfterMidnightOf(day.AddDays(1)));
        await AssertRefusedAsync(await StartAsync(draft, Anouk), HttpStatusCode.Conflict, "TripNotDispatched");
        tms.SetClock(ShortlyAfterMidnightOf(day));
        (await StartAsync(trip, Joost)).StatusCode.Should().Be(HttpStatusCode.OK);
        await AssertRefusedAsync(await StartAsync(trip, Joost), HttpStatusCode.Conflict, "TripNotDispatched");
        await TmsFixture.SettleOutboxAsync(_cancellationToken);
        tms.Published.Count(m => m.Subject == "TripStarted" && m.ApplicationProperties.TryGetValue("tripNumber", out var number) && number.Equals(trip)).Should().Be(1);
    }

    [Fact]
    public async Task CallersWithoutTripsStart_AreRefused_AndAStartWithoutADriverOrACorrelationId_IsABadRequest()
    {
        var (trip, day, _) = await DispatchedTripAsync(6300);
        tms.SetClock(ShortlyAfterMidnightOf(day));

        foreach (var caller in new[] { tms.TmsUi, tms.Tms, tms.TripDeliveryReport })
        {
            (await StartAsync(trip, Joost, caller: caller)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        }

        using var anonymous = tms.CreateTmsClient(token: null);
        (await StartAsync(trip, Joost, caller: anonymous)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await StartAsync(trip, driverId: null)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await StartAsync(trip, Joost, correlationId: null)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await GetTripAsync(trip))["trip"]!["status"]!.GetValue<string>().Should().Be("Dispatched");
    }

    [Fact]
    public async Task AfterStart_EveryAmendButTakingOffAnUncollectedShipment_IsTripUnderWay()
    {
        var (trip, day, shipment) = await DispatchedTripAsync(6400);
        var other = await ReleasedAsync(day);
        tms.SetClock(ShortlyAfterMidnightOf(day));
        (await StartAsync(trip, Joost)).StatusCode.Should().Be(HttpStatusCode.OK);

        await AssertRefusedAsync(await tms.TmsUi.PostAsJsonAsync(new Uri($"/v1/trips/{trip}/shipments", UriKind.Relative), new { shipmentNumbers = new[] { other } }, _cancellationToken), HttpStatusCode.Conflict, "TripUnderWay");
        await AssertRefusedAsync(
            await tms.TmsUi.PutAsJsonAsync(
                new Uri($"/v1/trips/{trip}/stops", UriKind.Relative),
                new { stops = new[] { new { shipmentNumber = shipment, kind = "Collection" }, new { shipmentNumber = shipment, kind = "Delivery" } } },
                _cancellationToken),
            HttpStatusCode.Conflict,
            "TripUnderWay");
        await AssertRefusedAsync(
            await tms.TmsUi.PutAsJsonAsync(new Uri($"/v1/trips/{trip}", UriKind.Relative), new { vehicleId = Truck, driverId = Joost, plannedStart = "09:30", plannedDrivingDuration = "04:00:00" }, _cancellationToken),
            HttpStatusCode.Conflict,
            "TripUnderWay");

        using var tookOff = await tms.TmsUi.PostAsJsonAsync(new Uri($"/v1/trips/{trip}/shipments/{shipment}/take-off", UriKind.Relative), new { reason = "Customer cancelled" }, _cancellationToken);
        tookOff.StatusCode.Should().Be(HttpStatusCode.OK, await tookOff.Content.ReadAsStringAsync(_cancellationToken));
    }

    private async Task<(string Trip, DateOnly Day, string Shipment)> DispatchedTripAsync(int daysAhead)
    {
        var day = (await OpenHorizonAsync(daysAhead)).AddDays(1);
        var shipment = await ReleasedAsync(day);
        var trip = await NumberOf(await CreateAsync(day, Truck, Joost, "09:00", "04:00:00", shipment));
        using var dispatched = await tms.TmsUi.PostAsync(new Uri($"/v1/trips/{trip}/dispatch", UriKind.Relative), content: null, _cancellationToken);
        var body = await dispatched.Content.ReadAsStringAsync(_cancellationToken);
        dispatched.StatusCode.Should().Be(HttpStatusCode.OK, body);
        await tms.PublishedAsync(JsonNode.Parse(body)!["correlationId"]!.GetValue<string>(), "TripDispatched", _cancellationToken);
        return (trip, day, shipment);
    }

    private Task<HttpResponseMessage> StartAsync(string trip, string? driverId, string? correlationId = "", HttpClient? caller = null, string? driverAppEventId = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, new Uri($"/v1/trips/{trip}/start", UriKind.Relative))
        {
            Content = JsonContent.Create(new JsonObject { ["driverId"] = driverId, ["driverAppEventId"] = driverAppEventId }),
        };
        if (correlationId is not null)
        {
            request.Headers.Add("X-Correlation-ID", correlationId.Length == 0 ? TmsCalls.NewCorrelationId() : correlationId);
        }

        return (caller ?? tms.TripStart).SendAsync(request, _cancellationToken);
    }

    private async Task<JsonNode> GetTripAsync(string number)
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

    private async Task<DateOnly> OpenHorizonAsync(int daysAhead)
    {
        var first = _realToday.AddDays(daysAhead);
        first = first.AddDays(((int)DayOfWeek.Monday - (int)first.DayOfWeek + 7) % 7);
        tms.SetClock(ShortlyAfterMidnightOf(first));
        await tms.HorizonStartingAtAsync(first, _cancellationToken);
        return first;
    }

    // A Released Own fleet Shipment that collects from 08:00 on the day, 2 pallets of 500 kg.
    private async Task<string> ReleasedAsync(DateOnly day)
    {
        var from = new DateTimeOffset(day.ToDateTime(new TimeOnly(8, 0)), Amsterdam.GetUtcOffset(day.ToDateTime(new TimeOnly(8, 0))));
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
