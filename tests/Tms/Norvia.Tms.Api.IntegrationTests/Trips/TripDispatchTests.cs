using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Norvia.Tms.Api.IntegrationTests.Planning;
using Norvia.Tms.Api.IntegrationTests.ShipperTracking;
using Norvia.Tms.Api.IntegrationTests.TrackingEvents;
using Norvia.Tms.Api.IntegrationTests.TransportOrders;
using static Norvia.Tms.Api.IntegrationTests.Capacity.CapacityCalls;

namespace Norvia.Tms.Api.IntegrationTests.Trips;

/// <summary>
/// Dispatching a Trip (P11) at the TMS's boundary: a Trip that lacks a part stays a Draft with TripIncomplete for each part, a Draft that became
/// invalid fails the re-check, and a successful Dispatch sets the Trip and its Shipments to Dispatched and publishes exactly one TripDispatched
/// version 1 under the Dispatch's correlation ID. Each test moves the clock to a stretch of days of its own.
/// </summary>
[Trait("AppHost", "Own4")]
public sealed class TripDispatchTests(TripsTmsFixture tms) : IClassFixture<TripsTmsFixture>, IDisposable
{
    private const string Truck = "VEH-0002";
    private const string Joost = "DRV-0002";

    private static readonly TimeZoneInfo Amsterdam = TimeZoneInfo.FindSystemTimeZoneById("Europe/Amsterdam");

    private readonly CancellationToken _cancellationToken = TestContext.Current.CancellationToken;
    private readonly DateOnly _realToday = Today(DateTimeOffset.UtcNow);

    public void Dispose() => tms.ResetClock();

    [Fact]
    public async Task ATripWithoutAPart_IsRefusedWithTripIncompleteForEachMissingPart_AndStaysADraft()
    {
        var day = (await OpenHorizonAsync(4000)).AddDays(1);
        var shipment = await ReleasedAsync(day);
        var trip = await NumberOf(await CreateAsync(day, Truck, null, null, null, shipment));

        using var refused = await DispatchAsync(trip);

        await AssertRefusedAsync(refused, HttpStatusCode.Conflict, "TripIncomplete");
        var failures = (await FailuresOf(refused)).Select(failure => failure["message"]!.GetValue<string>()).ToList();
        failures.Should().HaveCount(3);
        failures.Should().Contain(message => message.Contains("a Driver", StringComparison.Ordinal))
            .And.Contain(message => message.Contains("a planned start", StringComparison.Ordinal))
            .And.Contain(message => message.Contains("a planned driving duration", StringComparison.Ordinal));
        (await GetTripAsync(trip))["status"]!.GetValue<string>().Should().Be("Draft");
        (await ShipmentStatusAsync(shipment)).Should().Be("Released");
    }

    [Fact]
    public async Task AnEmptyTrip_IsRefusedWithoutAShipment()
    {
        var day = (await OpenHorizonAsync(4100)).AddDays(1);
        var trip = await NumberOf(await CreateAsync(day, Truck, Joost, "09:00", "04:00:00"));

        using var refused = await DispatchAsync(trip);

        await AssertRefusedAsync(refused, HttpStatusCode.Conflict, "TripIncomplete");
        (await FailuresOf(refused)).Should().ContainSingle().Which["message"]!.GetValue<string>().Should().Contain("at least one Shipment");
    }

    [Fact]
    public async Task ADraftWhoseDriverGotAnAbsence_FailsTheRecheck_AndNothingChanges()
    {
        var day = (await OpenHorizonAsync(4200)).AddDays(1);
        var shipment = await ReleasedAsync(day);
        var trip = await NumberOf(await CreateAsync(day, Truck, Joost, "09:00", "04:00:00", shipment));
        using var absence = await tms.RecordPeriodAsync("/v1/capacity/absences", Joost, day, day, "Sickness", _cancellationToken);
        absence.StatusCode.Should().Be(HttpStatusCode.OK, await absence.Content.ReadAsStringAsync(_cancellationToken));

        using var refused = await DispatchAsync(trip);

        await AssertRefusedAsync(refused, HttpStatusCode.Conflict, "DriverNotAvailable");
        (await GetTripAsync(trip))["status"]!.GetValue<string>().Should().Be("Draft");
        (await ShipmentStatusAsync(shipment)).Should().Be("Released");
        tms.Published.Where(message => message.Subject == "TripDispatched" && message.ApplicationProperties["tripNumber"].Equals(trip)).Should().BeEmpty();
    }

    [Fact]
    public async Task ACompleteTrip_IsDispatched_ItsShipmentsToo_AndExactlyOneTripDispatchedIsPublishedWithTheWholeTrip()
    {
        var day = (await OpenHorizonAsync(4300)).AddDays(1);
        var first = await ReleasedAsync(day, collectFrom: 8);
        var second = await ReleasedAsync(day, collectFrom: 9);
        var trip = await NumberOf(await CreateAsync(day, Truck, Joost, "09:00", "04:00:00", first, second));
        using var reordered = await tms.TmsUi.PutAsJsonAsync(
            new Uri($"/v1/trips/{trip}/stops", UriKind.Relative),
            new
            {
                stops = new[] { ("Collection", second), ("Collection", first), ("Delivery", first), ("Delivery", second) }
                    .Select(stop => new { shipmentNumber = stop.Item2, kind = stop.Item1 }),
            },
            _cancellationToken);
        reordered.StatusCode.Should().Be(HttpStatusCode.NoContent);

        using var dispatched = await DispatchAsync(trip);

        var answer = JsonNode.Parse(await dispatched.Content.ReadAsStringAsync(_cancellationToken))!;
        dispatched.StatusCode.Should().Be(HttpStatusCode.OK, answer.ToJsonString());
        answer["version"]!.GetValue<int>().Should().Be(1);
        var correlationId = answer["correlationId"]!.GetValue<string>();
        (await GetTripAsync(trip))["status"]!.GetValue<string>().Should().Be("Dispatched");
        (await ShipmentStatusAsync(first)).Should().Be("Dispatched");
        (await ShipmentStatusAsync(second)).Should().Be("Dispatched");

        var events = await tms.PublishedAsync(correlationId, "TripDispatched", _cancellationToken);
        var message = events.Should().ContainSingle("a Dispatch publishes TripDispatched once, and there is no event per Shipment").Subject;
        message.ApplicationProperties["tripNumber"].Should().Be(trip);
        message.ApplicationProperties["flow"].Should().Be("trip-dispatch");
        var body = JsonNode.Parse(message.Body.ToString())!;
        var results = CanonicalModelSchemas.ValidateTripDispatched(body);
        results.IsValid.Should().BeTrue(CanonicalModelSchemas.Errors(results));
        body["correlationId"]!.GetValue<string>().Should().Be(correlationId);
        body["version"]!.GetValue<int>().Should().Be(1);
        body["tripNumber"]!.GetValue<string>().Should().Be(trip);
        body["vehicle"]!["vehicleId"]!.GetValue<string>().Should().Be(Truck);
        body["driver"]!["driverId"]!.GetValue<string>().Should().Be(Joost);
        body["plannedStart"]!.GetValue<string>().Should().Be("09:00:00");
        body["plannedDrivingDuration"]!.GetValue<string>().Should().Be("PT4H");
        body["stops"]!.AsArray().Select(stop => $"{stop!["sequence"]} {stop["kind"]} {stop["shipmentNumber"]}").Should().Equal(
            $"1 Collection {second}", $"2 Collection {first}", $"3 Delivery {first}", $"4 Delivery {second}");
        tms.Published.Count(m => m.Subject == "TripDispatched" && m.CorrelationId == correlationId).Should().Be(1);
        tms.Published.Where(m => m.CorrelationId == correlationId).Should().OnlyContain(m => m.Subject == "TripDispatched", "nothing is published per Shipment");
    }

    [Fact]
    public async Task ADispatchedTrip_DispatchedAgain_IsRefusedWithTripNotDraft_AndPublishesNothingMore()
    {
        var day = (await OpenHorizonAsync(4400)).AddDays(1);
        var shipment = await ReleasedAsync(day);
        var trip = await NumberOf(await CreateAsync(day, Truck, Joost, "09:00", "04:00:00", shipment));
        using var first = await DispatchAsync(trip);
        var correlationId = JsonNode.Parse(await first.Content.ReadAsStringAsync(_cancellationToken))!["correlationId"]!.GetValue<string>();
        await tms.PublishedAsync(correlationId, "TripDispatched", _cancellationToken);

        using var again = await DispatchAsync(trip);

        await AssertRefusedAsync(again, HttpStatusCode.Conflict, "TripNotDraft");
        tms.Published.Count(m => m.Subject == "TripDispatched" && m.ApplicationProperties["tripNumber"].Equals(trip)).Should().Be(1);
    }

    [Fact]
    public async Task ADispatchedShipment_ShowsAsScheduledToTheShipper_AndIsStaleWhenNotCollectedInTime_WithoutAnythingHappeningAutomatically()
    {
        var day = (await OpenHorizonAsync(4500)).AddDays(1);
        var shipment = await ReleasedAsync(day);
        var trip = await NumberOf(await CreateAsync(day, Truck, Joost, "09:00", "04:00:00", shipment));
        using var dispatched = await DispatchAsync(trip);
        dispatched.StatusCode.Should().Be(HttpStatusCode.OK, await dispatched.Content.ReadAsStringAsync(_cancellationToken));
        var order = await tms.TransportOrderOfAsync(shipment, _cancellationToken);

        var tracking = await tms.GotAsync(ShipperTrackingCalls.Shipper, order, _cancellationToken);

        tracking["trackingStatus"]!.GetValue<string>().Should().Be("Scheduled");
        tracking.ToJsonString().Should().NotContain("Dispatched").And.NotContain(trip, "nothing about Norvia's Trips leaks into tracking");
        var collectionLatest = tracking["collectionWindow"]!["latest"]!.GetValue<DateTimeOffset>();

        tms.SetClock(collectionLatest.AddHours(2).AddTicks(-1));
        (await InProgressAsync(shipment))["isStale"]!.GetValue<bool>().Should().BeFalse();

        tms.SetClock(collectionLatest.AddHours(2));
        var stale = await InProgressAsync(shipment);
        stale["isStale"]!.GetValue<bool>().Should().BeTrue();
        stale["status"]!.GetValue<string>().Should().Be("Dispatched");
        (await GetTripAsync(trip))["status"]!.GetValue<string>().Should().Be("Dispatched", "Stale is only a flag");
        (await tms.GotAsync(ShipperTrackingCalls.Shipper, order, _cancellationToken))["trackingStatus"]!.GetValue<string>().Should().Be("Scheduled");
    }

    [Fact]
    public async Task AnUnknownTrip_IsTripNotFound()
    {
        using var refused = await DispatchAsync("TR-9999999");

        await AssertRefusedAsync(refused, HttpStatusCode.NotFound, "TripNotFound");
    }

    private Task<HttpResponseMessage> DispatchAsync(string trip) =>
        tms.TmsUi.PostAsync(new Uri($"/v1/trips/{trip}/dispatch", UriKind.Relative), content: null, _cancellationToken);

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

    private async Task<JsonObject> GetTripAsync(string number)
    {
        using var response = await tms.TmsUi.GetAsync(new Uri($"/v1/trips/{number}", UriKind.Relative), _cancellationToken);
        var body = await response.Content.ReadAsStringAsync(_cancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK, body);
        return JsonNode.Parse(body)!["trip"]!.AsObject();
    }

    private async Task<JsonNode> InProgressAsync(string shipment) =>
        (await tms.ListShipmentsInProgressAsync(_cancellationToken)).Single(listed => listed!["shipmentNumber"]!.GetValue<string>() == shipment)!;

    private async Task<string> ShipmentStatusAsync(string shipment)
    {
        using var response = await tms.TmsUi.GetAsync(new Uri($"/v1/shipments/{shipment}", UriKind.Relative), _cancellationToken);
        var body = await response.Content.ReadAsStringAsync(_cancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK, body);
        return JsonNode.Parse(body)!["status"]!.GetValue<string>();
    }

    private async Task<IReadOnlyList<JsonNode>> FailuresOf(HttpResponseMessage response) =>
        [.. JsonNode.Parse(await response.Content.ReadAsStringAsync(_cancellationToken))!["failures"]!.AsArray().Select(failure => failure!)];

    private async Task AssertRefusedAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        var body = await response.Content.ReadAsStringAsync(_cancellationToken);
        response.StatusCode.Should().Be(status, body);
        var problem = JsonNode.Parse(body)!.AsObject();
        problem["code"]!.GetValue<string>().Should().Be(code);
        problem["detail"]!.GetValue<string>().Should().NotBeNullOrWhiteSpace("the refusal has a readable message");
    }
}
