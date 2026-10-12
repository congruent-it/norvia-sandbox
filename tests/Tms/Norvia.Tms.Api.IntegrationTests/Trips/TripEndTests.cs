using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Norvia.Tms.Api.IntegrationTests.Planning;
using Norvia.Tms.Api.IntegrationTests.TrackingEvents;
using Norvia.Tms.Api.IntegrationTests.TransportOrders;
using static Norvia.Tms.Api.IntegrationTests.Capacity.CapacityCalls;

namespace Norvia.Tms.Api.IntegrationTests.Trips;

/// <summary>
/// Ending an In progress Trip (P12) at the TMS's boundary: the Driver reports End under the role Trips.End. With every Stop given an outcome the Trip
/// is Completed and publishes exactly one TripCompleted under the Driver's correlation ID, listing the Shipments delivered, not delivered and not
/// collected. An uncollected Shipment is Planned again and a collected one that wasn't delivered is Awaiting re-delivery, keeping its status, both
/// off the Trip and without ShipmentTakenOffTrip. The rules refuse the rest. The clock is controlled, so each test sets the day it works on.
/// </summary>
[Trait("AppHost", "Own4")]
public sealed class TripEndTests(TripsTmsFixture tms) : IClassFixture<TripsTmsFixture>, IDisposable
{
    private const string Truck = "VEH-0002";
    private const string Joost = "DRV-0002";
    private const string Anouk = "DRV-0001";

    private static readonly TimeZoneInfo Amsterdam = TimeZoneInfo.FindSystemTimeZoneById("Europe/Amsterdam");

    private readonly CancellationToken _cancellationToken = TestContext.Current.CancellationToken;
    private readonly DateOnly _realToday = Today(DateTimeOffset.UtcNow);

    public void Dispose() => tms.ResetClock();

    [Fact]
    public async Task EndingATrip_CompletesIt_PublishesExactlyOneTripCompletedWithTheThreeGroups_AndDealsWithTheUnfinishedShipments()
    {
        var (trip, day, shipments) = await StartedTripAsync(8000, shipmentCount: 3);
        var (delivered, notDelivered, notCollected) = (shipments[0], shipments[1], shipments[2]);
        var now = ShortlyAfterMidnightOf(day);
        await RecordAsync(Report("Collected", trip, delivered, now.AddMinutes(-30)));
        await RecordAsync(Report("Delivered", trip, delivered, now.AddMinutes(-20)));
        await RecordAsync(Report("Collected", trip, notDelivered, now.AddMinutes(-30)));
        await RecordAsync(Report("DeliveryFailed", trip, notDelivered, now.AddMinutes(-20)));
        await RecordAsync(Report("CollectionFailed", trip, notCollected, now.AddMinutes(-30)));
        var correlationId = TmsCalls.NewCorrelationId();

        using var ended = await EndAsync(trip, Joost, correlationId);

        var answer = JsonNode.Parse(await ended.Content.ReadAsStringAsync(_cancellationToken))!;
        ended.StatusCode.Should().Be(HttpStatusCode.OK, answer.ToJsonString());
        answer["version"]!.GetValue<int>().Should().Be(1);
        (await GetTripAsync(trip))["trip"]!["status"]!.GetValue<string>().Should().Be("Completed");
        (await ListedAsync(trip))["status"]!.GetValue<string>().Should().Be("Completed");

        var message = (await tms.PublishedAsync(correlationId, "TripCompleted", _cancellationToken)).Should().ContainSingle().Subject;
        message.ApplicationProperties["tripNumber"].Should().Be(trip);
        message.ApplicationProperties["flow"].Should().Be("trip-execution");
        var body = JsonNode.Parse(message.Body.ToString())!;
        var results = CanonicalModelSchemas.ValidateTripCompleted(body);
        results.IsValid.Should().BeTrue(CanonicalModelSchemas.Errors(results));
        body["tripNumber"]!.GetValue<string>().Should().Be(trip);
        body["driverId"]!.GetValue<string>().Should().Be(Joost);
        body["correlationId"]!.GetValue<string>().Should().Be(correlationId);
        Numbers(body["deliveredShipments"]).Should().Equal(delivered);
        Numbers(body["notDeliveredShipments"]).Should().Equal(notDelivered);
        Numbers(body["notCollectedShipments"]).Should().Equal(notCollected);

        (await ShipmentAsync(delivered))["status"]!.GetValue<string>().Should().Be("Delivered");
        var awaiting = await ShipmentAsync(notDelivered);
        awaiting["status"]!.GetValue<string>().Should().Be("Collected");
        awaiting["awaitingRedelivery"]!.GetValue<bool>().Should().BeTrue();
        var planned = await ShipmentAsync(notCollected);
        planned["status"]!.GetValue<string>().Should().Be("Planned");
        planned["awaitingRedelivery"]!.GetValue<bool>().Should().BeFalse();
        (await GetTripAsync(trip))["stops"]!.AsArray().Select(stop => stop!["shipmentNumber"]!.GetValue<string>()).Distinct().Should().Equal(delivered);

        await TmsFixture.SettleOutboxAsync(_cancellationToken);
        tms.Published.Count(m => m.Subject == "TripCompleted" && m.ApplicationProperties.TryGetValue("tripNumber", out var number) && number.Equals(trip)).Should().Be(1);
        tms.Published.Count(m => m.Subject == "ShipmentTakenOffTrip" && m.ApplicationProperties.TryGetValue("tripNumber", out var number) && number.Equals(trip)).Should().Be(0);
    }

    [Fact]
    public async Task AnOpenStop_IsStopsOpen_AndTheTripStaysInProgress_UntilEveryStopHasItsOutcome()
    {
        var (trip, day, shipments) = await StartedTripAsync(8100);
        var now = ShortlyAfterMidnightOf(day);
        await RecordAsync(Report("Collected", trip, shipments[0], now.AddMinutes(-30)));

        await AssertRefusedAsync(await EndAsync(trip, Joost), HttpStatusCode.Conflict, "StopsOpen");

        (await GetTripAsync(trip))["trip"]!["status"]!.GetValue<string>().Should().Be("InProgress");
        await RecordAsync(Report("Delivered", trip, shipments[0], now.AddMinutes(-20)));
        (await EndAsync(trip, Joost)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task AnotherDriver_AndAnUnknownTrip_AreTripNotFound_AndADispatchedOrEndedTrip_IsTripNotUnderWay()
    {
        var (trip, day, shipments) = await StartedTripAsync(8200);
        var (dispatched, _, _) = await DispatchedTripAsync(8250);
        tms.SetClock(ShortlyAfterMidnightOf(day));
        await RecordAsync(Report("CollectionFailed", trip, shipments[0], ShortlyAfterMidnightOf(day).AddMinutes(-30)));

        await AssertRefusedAsync(await EndAsync(trip, Anouk), HttpStatusCode.NotFound, "TripNotFound");
        await AssertRefusedAsync(await EndAsync("TR-9999999", Joost), HttpStatusCode.NotFound, "TripNotFound");
        await AssertRefusedAsync(await EndAsync(dispatched, Joost), HttpStatusCode.Conflict, "TripNotUnderWay");
        (await EndAsync(trip, Joost)).StatusCode.Should().Be(HttpStatusCode.OK);
        await AssertRefusedAsync(await EndAsync(trip, Joost), HttpStatusCode.Conflict, "TripNotUnderWay");
        await TmsFixture.SettleOutboxAsync(_cancellationToken);
        tms.Published.Count(m => m.Subject == "TripCompleted" && m.ApplicationProperties.TryGetValue("tripNumber", out var number) && number.Equals(trip)).Should().Be(1);
    }

    [Fact]
    public async Task CallersWithoutTripsEnd_AreRefused_AndAnEndWithoutADriverOrACorrelationId_IsABadRequest()
    {
        var (trip, _, shipments) = await StartedTripAsync(8300);
        await RecordAsync(Report("CollectionFailed", trip, shipments[0], DateTimeOffset.UtcNow.AddYears(-1)));

        foreach (var caller in new[] { tms.TmsUi, tms.Tms, tms.TripDeliveryReport, tms.TripStart, tms.TrackingRecord })
        {
            (await EndAsync(trip, Joost, caller: caller)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        }

        using var anonymous = tms.CreateTmsClient(token: null);
        (await EndAsync(trip, Joost, caller: anonymous)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await EndAsync(trip, driverId: null)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await EndAsync(trip, Joost, correlationId: null)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await GetTripAsync(trip))["trip"]!["status"]!.GetValue<string>().Should().Be("InProgress");
    }

    private static IEnumerable<string> Numbers(JsonNode? array) => array!.AsArray().Select(number => number!.GetValue<string>());

    private Task<HttpResponseMessage> EndAsync(string trip, string? driverId, string? correlationId = "", HttpClient? caller = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, new Uri($"/v1/trips/{trip}/end", UriKind.Relative))
        {
            Content = JsonContent.Create(new JsonObject { ["driverId"] = driverId }),
        };
        if (correlationId is not null)
        {
            request.Headers.Add("X-Correlation-ID", correlationId.Length == 0 ? TmsCalls.NewCorrelationId() : correlationId);
        }

        return (caller ?? tms.TripEnd).SendAsync(request, _cancellationToken);
    }

    private async Task RecordAsync(JsonObject report)
    {
        using var response = await tms.RecordAsync(tms.DriverReports, report, _cancellationToken);
        response.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.Accepted, HttpStatusCode.Created, HttpStatusCode.NoContent);
    }

    private static JsonObject Report(string type, string trip, string shipment, DateTimeOffset eventTime, string driver = Joost, int version = 1, string? remark = null)
    {
        var report = TrackingEventCalls.Event(type, "unused", eventTime, source: new JsonObject { ["type"] = "OwnFleet", ["driverId"] = driver });
        report.Remove("carrierBookingReference");
        report["tripNumber"] = trip;
        report["shipmentNumber"] = shipment;
        report["tripVersion"] = version;
        report["location"] = new JsonObject { ["place"] = "Utrecht", ["postcode"] = "3511 AW", ["countryCode"] = "NL" };
        report["remark"] = remark ?? $"{type} at the Stop";
        return report;
    }

    private async Task<(string Trip, DateOnly Day, string[] Shipments)> StartedTripAsync(int daysAhead, int shipmentCount = 1)
    {
        var (trip, day, shipments) = await DispatchedTripAsync(daysAhead, shipmentCount);
        tms.SetClock(ShortlyAfterMidnightOf(day));
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri($"/v1/trips/{trip}/start", UriKind.Relative))
        {
            Content = JsonContent.Create(new JsonObject { ["driverId"] = Joost }),
        };
        request.Headers.Add("X-Correlation-ID", TmsCalls.NewCorrelationId());
        using var started = await tms.TripStart.SendAsync(request, _cancellationToken);
        started.StatusCode.Should().Be(HttpStatusCode.OK, await started.Content.ReadAsStringAsync(_cancellationToken));
        return (trip, day, shipments);
    }

    private async Task<(string Trip, DateOnly Day, string[] Shipments)> DispatchedTripAsync(int daysAhead, int shipmentCount = 1)
    {
        var day = (await OpenHorizonAsync(daysAhead)).AddDays(1);
        var shipments = new string[shipmentCount];
        for (var i = 0; i < shipmentCount; i++)
        {
            shipments[i] = await ReleasedAsync(day);
        }

        var trip = await NumberOf(await CreateAsync(day, Truck, Joost, "09:00", "04:00:00", shipments));
        using var dispatched = await tms.TmsUi.PostAsync(new Uri($"/v1/trips/{trip}/dispatch", UriKind.Relative), content: null, _cancellationToken);
        var body = await dispatched.Content.ReadAsStringAsync(_cancellationToken);
        dispatched.StatusCode.Should().Be(HttpStatusCode.OK, body);
        await tms.PublishedAsync(JsonNode.Parse(body)!["correlationId"]!.GetValue<string>(), "TripDispatched", _cancellationToken);
        return (trip, day, shipments);
    }

    private async Task<JsonNode> ShipmentAsync(string shipment)
    {
        using var response = await tms.TmsUi.GetAsync(new Uri($"/v1/shipments/{shipment}", UriKind.Relative), _cancellationToken);
        var body = await response.Content.ReadAsStringAsync(_cancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK, body);
        return JsonNode.Parse(body)!;
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
