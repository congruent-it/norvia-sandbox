using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Norvia.Tms.Api.IntegrationTests.Planning;
using Norvia.Tms.Api.IntegrationTests.TransportOrders;
using static Norvia.Tms.Api.IntegrationTests.Capacity.CapacityCalls;

namespace Norvia.Tms.Api.IntegrationTests.Trips;

/// <summary>
/// Putting the Stops of a Draft Trip in order (P11) at the TMS's boundary: a new order is saved, a delivery before its collection is refused with
/// CollectionBeforeDelivery and leaves the order unchanged, and two Stops at the same address stay two Stops. Each test moves the clock to a stretch
/// of days of its own.
/// </summary>
[Trait("AppHost", "Own4")]
public sealed class TripStopOrderTests(TripsTmsFixture tms) : IClassFixture<TripsTmsFixture>, IDisposable
{
    private const string Truck = "VEH-0002";

    private static readonly TimeZoneInfo Amsterdam = TimeZoneInfo.FindSystemTimeZoneById("Europe/Amsterdam");

    private readonly CancellationToken _cancellationToken = TestContext.Current.CancellationToken;
    private readonly DateOnly _realToday = Today(DateTimeOffset.UtcNow);

    public void Dispose() => tms.ResetClock();

    [Fact]
    public async Task ANewOrder_IsSaved_AndSurvivesAReread_AndTwoStopsAtTheSameAddressStayTwoStops()
    {
        var day = (await OpenHorizonAsync(3600)).AddDays(1);
        var (trip, first, second) = await TripWithTwoShipmentsAsync(day);

        using var reordered = await ReorderAsync(trip, ("Collection", second), ("Collection", first), ("Delivery", second), ("Delivery", first));

        reordered.StatusCode.Should().Be(HttpStatusCode.NoContent, await reordered.Content.ReadAsStringAsync(_cancellationToken));
        var stops = Stops(await GetTripAsync(trip)).ToList();
        stops.Should().Equal($"Collection {second}", $"Collection {first}", $"Delivery {second}", $"Delivery {first}");
        (await GetTripAsync(trip))["stops"]!.AsArray().Select(stop => stop!["place"]!.GetValue<string>()).Take(2).Distinct().Should()
            .ContainSingle("both Shipments collect at the same address, and each still has its own Stop");
        using var again = await ReorderAsync(trip, ("Collection", second), ("Collection", first), ("Delivery", second), ("Delivery", first));
        again.StatusCode.Should().Be(HttpStatusCode.NoContent, "the same order sent again changes nothing");
    }

    [Fact]
    public async Task ADeliveryBeforeItsCollection_IsRefusedWithCollectionBeforeDelivery_AndTheOrderIsUnchanged()
    {
        var day = (await OpenHorizonAsync(3700)).AddDays(1);
        var (trip, first, second) = await TripWithTwoShipmentsAsync(day);

        using var refused = await ReorderAsync(trip, ("Delivery", first), ("Collection", first), ("Collection", second), ("Delivery", second));

        await AssertRefusedAsync(refused, HttpStatusCode.Conflict, "CollectionBeforeDelivery");
        Stops(await GetTripAsync(trip)).Should().Equal($"Collection {first}", $"Delivery {first}", $"Collection {second}", $"Delivery {second}");
    }

    [Fact]
    public async Task AnOrderThatIsNotTheTripsStops_IsRefusedWithStopsNotTheTripsStops()
    {
        var day = (await OpenHorizonAsync(3800)).AddDays(1);
        var (trip, first, _) = await TripWithTwoShipmentsAsync(day);

        using var refused = await ReorderAsync(trip, ("Collection", first), ("Delivery", first));

        await AssertRefusedAsync(refused, HttpStatusCode.Conflict, "StopsNotTheTripsStops");
    }

    [Fact]
    public async Task AnUnknownTrip_IsTripNotFound_AndABodyWithoutStops_IsABadRequest()
    {
        using var unknown = await ReorderAsync("TR-9999999", ("Collection", "SH-0000001"));
        using var empty = await tms.TmsUi.PutAsJsonAsync(new Uri("/v1/trips/TR-9999999/stops", UriKind.Relative), new { stops = (object[])[] }, _cancellationToken);

        await AssertRefusedAsync(unknown, HttpStatusCode.NotFound, "TripNotFound");
        empty.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    private async Task<(string Trip, string First, string Second)> TripWithTwoShipmentsAsync(DateOnly day)
    {
        var first = await ReleasedAsync(day);
        var second = await ReleasedAsync(day);
        var trip = await NumberOf(await CreateAsync(day, Truck, null, first, second));
        return (trip, first, second);
    }

    private Task<HttpResponseMessage> ReorderAsync(string trip, params (string Kind, string Shipment)[] order) =>
        tms.TmsUi.PutAsJsonAsync(
            new Uri($"/v1/trips/{trip}/stops", UriKind.Relative),
            new { stops = order.Select(stop => new { shipmentNumber = stop.Shipment, kind = stop.Kind }) },
            _cancellationToken);

    private async Task<DateOnly> OpenHorizonAsync(int daysAhead)
    {
        var first = _realToday.AddDays(daysAhead);
        first = first.AddDays(((int)DayOfWeek.Monday - (int)first.DayOfWeek + 7) % 7);
        tms.SetClock(ShortlyAfterMidnightOf(first));
        await tms.HorizonStartingAtAsync(first, _cancellationToken);
        return first;
    }

    // A Planned Own fleet Shipment that collects from the hour on the day, with 2 pallets of 500 kg in all unless said otherwise.
    private async Task<string> PlannedAsync(DateOnly day, int collectFrom = 8, int pallets = 2, decimal weightKg = 500m, DateOnly? deliveryDay = null, bool toCarrier = false)
    {
        var from = new DateTimeOffset(day.ToDateTime(new TimeOnly(collectFrom, 0)), Amsterdam.GetUtcOffset(day.ToDateTime(new TimeOnly(collectFrom, 0))));
        var booking = Bookings.Valid(TmsCalls.NewReference()).CollectedFrom(from);
        booking["goodsLines"] = new JsonArray { new JsonObject { ["packageType"] = "pallet", ["quantity"] = pallets, ["weightKg"] = weightKg, ["dimensionsCm"] = null } };
        if (deliveryDay is { } delivery)
        {
            var at = new DateTimeOffset(delivery.ToDateTime(new TimeOnly(10, 0)), Amsterdam.GetUtcOffset(delivery.ToDateTime(new TimeOnly(10, 0))));
            booking["delivery"]!["window"] = new JsonObject { ["earliest"] = at.ToString("O"), ["latest"] = at.AddHours(4).ToString("O") };
        }

        var number = await tms.SubmitAcceptedAsync(booking, TmsCalls.NewCorrelationId(), _cancellationToken);
        await tms.WaitForRatingResultAsync(number, "Rated", _cancellationToken);
        var assigned = await tms.AssignPlannedAsync(number, toCarrier ? PlanningCalls.ToCarrier() : PlanningCalls.ToOwnFleet(), _cancellationToken);
        return assigned["shipmentNumber"]!.GetValue<string>();
    }

    private async Task<string> ReleasedAsync(
        DateOnly day, int collectFrom = 8, int pallets = 2, decimal weightKg = 500m, DateOnly? deliveryDay = null, bool toCarrier = false)
    {
        var shipment = await PlannedAsync(day, collectFrom, pallets, weightKg, deliveryDay, toCarrier);
        await tms.ReleasedAsync(shipment, _cancellationToken);
        return shipment;
    }

    private async Task<HttpResponseMessage> CreateAsync(DateOnly day, string? vehicleId, string? driverId, params string[] shipments)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri("/v1/trips", UriKind.Relative))
        {
            Content = JsonContent.Create(new { day = $"{day:yyyy-MM-dd}", vehicleId, driverId, shipmentNumbers = shipments.Length == 0 ? null : shipments }),
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
        return JsonNode.Parse(body)!.AsObject();
    }

    private static IEnumerable<string> Stops(JsonObject detail) =>
        detail["stops"]!.AsArray().Select(stop => $"{stop!["kind"]!.GetValue<string>()} {stop["shipmentNumber"]!.GetValue<string>()}");

    private async Task AssertRefusedAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        var body = await response.Content.ReadAsStringAsync(_cancellationToken);
        response.StatusCode.Should().Be(status, body);
        var problem = JsonNode.Parse(body)!.AsObject();
        problem["code"]!.GetValue<string>().Should().Be(code);
        problem["detail"]!.GetValue<string>().Should().NotBeNullOrWhiteSpace("the refusal has a readable message");
    }
}
