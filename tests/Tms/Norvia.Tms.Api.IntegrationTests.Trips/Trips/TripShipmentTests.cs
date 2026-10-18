using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using static Norvia.Tms.TestSupport.Capacity.CapacityCalls;

namespace Norvia.Tms.Api.IntegrationTests.Trips;

/// <summary>
/// Putting Shipments on Trips (P11) at the TMS's boundary: the list of Released Own fleet Shipments not on a Trip, a Trip created from Shipments or
/// added to, the Stops it gets, and the five refusals. The test seed's van VEH-0001 carries 1,200 kg and 4 pallets, the rigid truck VEH-0002 10,000 kg
/// and 18 pallets. Each test moves the clock to a stretch of days of its own.
/// </summary>
public sealed class TripShipmentTests(TripsTmsFixture tms) : IClassFixture<TripsTmsFixture>, IDisposable
{
    private const string Van = "VEH-0001";
    private const string Truck = "VEH-0002";
    private const string Anouk = "DRV-0001";

    private static readonly string[] Vehicles = [Van, Truck];
    private static readonly string[] UnknownShipment = ["SH-9999999"];
    private static readonly string[] KnownLooking = ["SH-0000001"];
    private static readonly TimeZoneInfo Amsterdam = TimeZoneInfo.FindSystemTimeZoneById("Europe/Amsterdam");

    private readonly CancellationToken _cancellationToken = TestContext.Current.CancellationToken;
    private readonly DateOnly _realToday = Today(DateTimeOffset.UtcNow);

    public void Dispose() => tms.ResetClock();

    [Fact]
    public async Task TheList_ShowsReleasedOwnFleetShipmentsNotOnATrip_ByCollectionTime_AndFilteredByDay()
    {
        var monday = await OpenHorizonAsync(3000);
        var tuesday = monday.AddDays(1);
        var later = await ReleasedAsync(tuesday, collectFrom: 10);
        var earlier = await ReleasedAsync(tuesday, collectFrom: 8);
        var otherDay = await ReleasedAsync(tuesday.AddDays(1));
        var planned = await PlannedAsync(tuesday);
        var onCarrier = await ReleasedAsync(tuesday, toCarrier: true);

        var onTuesday = await ListToPutOnTripsAsync(tuesday);
        var all = await ListToPutOnTripsAsync(null);

        Numbers(onTuesday).Should().Equal(earlier, later);
        Numbers(all).Should().Contain([earlier, later, otherDay]).And.NotContain([planned, onCarrier]);
        onTuesday[0]["weightKg"]!.GetValue<decimal>().Should().Be(500m);
        onTuesday[0]["palletPlaces"]!.GetValue<int>().Should().Be(2);
    }

    [Fact]
    public async Task ATripCreatedFromShipments_HasTheirCollectionAndDeliveryStopsInOrder_AndTheyLeaveTheList_AndStayReleased()
    {
        var day = (await OpenHorizonAsync(3100)).AddDays(1);
        var first = await ReleasedAsync(day);
        var second = await ReleasedAsync(day);

        using var created = await CreateAsync(day, Truck, null, first, second);

        created.StatusCode.Should().Be(HttpStatusCode.Created, await created.Content.ReadAsStringAsync(_cancellationToken));
        var trip = await NumberOf(created);
        var detail = await GetTripAsync(trip);
        Stops(detail).Should().Equal($"Collection {first}", $"Delivery {first}", $"Collection {second}", $"Delivery {second}");
        detail["trip"]!["stopCount"]!.GetValue<int>().Should().Be(4);
        detail["trip"]!["loadWeightKg"]!.GetValue<decimal>().Should().Be(1_000m);
        detail["trip"]!["payloadKg"]!.GetValue<int>().Should().Be(10_000);
        detail["stops"]!.AsArray()[0]!["place"]!.GetValue<string>().Should().Be("Waalhaven Oostzijde 88, 3087 BM Rotterdam");
        detail["stops"]!.AsArray()[1]!["name"]!.GetValue<string>().Should().Be("Café De Zwaan");
        Numbers(await ListToPutOnTripsAsync(day)).Should().NotContain([first, second]);
        (await GetShipmentAsync(first))["status"]!.GetValue<string>().Should().Be("Released");
        (await ListTripsAsync()).Single(item => item["number"]!.GetValue<string>() == trip)["stopCount"]!.GetValue<int>().Should().Be(4);
    }

    [Fact]
    public async Task AddingToAnExistingTrip_PutsTheStopsAtTheEnd_AndAddingAgainChangesNothing()
    {
        var day = (await OpenHorizonAsync(3200)).AddDays(1);
        var first = await ReleasedAsync(day);
        var second = await ReleasedAsync(day);
        var trip = await NumberOf(await CreateAsync(day, Truck, null, first));

        using var added = await AddAsync(trip, second);
        using var again = await AddAsync(trip, second, first);

        added.StatusCode.Should().Be(HttpStatusCode.OK, await added.Content.ReadAsStringAsync(_cancellationToken));
        again.StatusCode.Should().Be(HttpStatusCode.OK);
        Stops(await GetTripAsync(trip)).Should().Equal($"Collection {first}", $"Delivery {first}", $"Collection {second}", $"Delivery {second}");
    }

    [Fact]
    public async Task EachRefusal_IsReturnedWithItsCode_AndLeavesTheTripUnchanged()
    {
        var day = (await OpenHorizonAsync(3300)).AddDays(1);
        var vanTrip = await NumberOf(await CreateAsync(day, Van, null));
        var otherTrip = await NumberOf(await CreateAsync(day, Truck, null));
        var heavy = await ReleasedAsync(day, pallets: 1, weightKg: 1_500m);
        var manyPallets = await ReleasedAsync(day, pallets: 5, weightKg: 100m);
        var planned = await PlannedAsync(day);
        var wrongDay = await ReleasedAsync(day.AddDays(1));
        var taken = await ReleasedAsync(day);
        (await AddAsync(otherTrip, taken)).StatusCode.Should().Be(HttpStatusCode.OK);

        using var notReleased = await AddAsync(vanTrip, planned);
        using var onAnother = await AddAsync(vanTrip, taken);
        using var mismatch = await AddAsync(vanTrip, wrongDay);
        using var payload = await AddAsync(vanTrip, heavy);
        using var pallets = await AddAsync(vanTrip, manyPallets);

        await AssertRefusedAsync(notReleased, HttpStatusCode.Conflict, "ShipmentNotReleased");
        await AssertRefusedAsync(onAnother, HttpStatusCode.Conflict, "ShipmentOnAnotherTrip");
        await AssertRefusedAsync(mismatch, HttpStatusCode.Conflict, "CollectionDayMismatch");
        await AssertRefusedAsync(payload, HttpStatusCode.Conflict, "PayloadExceeded");
        await AssertRefusedAsync(pallets, HttpStatusCode.Conflict, "PalletPlacesExceeded");
        Stops(await GetTripAsync(vanTrip)).Should().BeEmpty();
    }

    [Fact]
    public async Task TheLoadCountsTheWholeTripAtOnce_SoTwoShipmentsThatFitAloneAreRefusedTogether()
    {
        var day = (await OpenHorizonAsync(3400)).AddDays(1);
        var trip = await NumberOf(await CreateAsync(day, Van, null));
        var first = await ReleasedAsync(day, pallets: 2, weightKg: 700m);
        var second = await ReleasedAsync(day, pallets: 2, weightKg: 700m);

        using var together = await AddAsync(trip, first, second);

        await AssertRefusedAsync(together, HttpStatusCode.Conflict, "PayloadExceeded");
        Stops(await GetTripAsync(trip)).Should().BeEmpty("neither went on");
        (await AddAsync(trip, first)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task ADeliveryWindowOutsideTheTripsDay_IsAcceptedWithAWarning_AndTheTripDetailShowsIt()
    {
        var day = (await OpenHorizonAsync(3500)).AddDays(1);
        var trip = await NumberOf(await CreateAsync(day, Truck, null));
        var late = await ReleasedAsync(day, deliveryDay: day.AddDays(2));

        using var added = await AddAsync(trip, late);

        added.StatusCode.Should().Be(HttpStatusCode.OK, await added.Content.ReadAsStringAsync(_cancellationToken));
        var warnings = JsonNode.Parse(await added.Content.ReadAsStringAsync(_cancellationToken))!["warnings"]!.AsArray();
        warnings.Should().ContainSingle().Which!["code"]!.GetValue<string>().Should().Be("DeliveryWindowOutsideTripDay");
        (await GetTripAsync(trip))["warnings"]!.AsArray().Should().ContainSingle();
    }

    [Fact]
    public async Task ARefusedCreate_CreatesNoTrip()
    {
        var day = (await OpenHorizonAsync(3600)).AddDays(1);
        var wrongDay = await ReleasedAsync(day.AddDays(1));

        using var refused = await CreateAsync(day, null, null, wrongDay);

        await AssertRefusedAsync(refused, HttpStatusCode.Conflict, "CollectionDayMismatch");
        (await ListTripsAsync()).Should().NotContain(trip => trip["day"]!.GetValue<string>() == $"{day:yyyy-MM-dd}");
    }

    [Fact]
    public async Task AnUnknownShipmentOrTrip_IsNotFound()
    {
        var day = (await OpenHorizonAsync(3700)).AddDays(1);
        var trip = await NumberOf(await CreateAsync(day, null, null));

        using var unknownShipment = await tms.TmsUi.PostAsJsonAsync(new Uri($"/v1/trips/{trip}/shipments", UriKind.Relative), new { shipmentNumbers = UnknownShipment }, _cancellationToken);
        using var unknownTrip = await tms.TmsUi.PostAsJsonAsync(new Uri("/v1/trips/TR-9999999/shipments", UriKind.Relative), new { shipmentNumbers = KnownLooking }, _cancellationToken);
        using var detail = await tms.TmsUi.GetAsync(new Uri("/v1/trips/TR-9999999", UriKind.Relative), _cancellationToken);

        await AssertRefusedAsync(unknownShipment, HttpStatusCode.NotFound, "ShipmentNotFound");
        await AssertRefusedAsync(unknownTrip, HttpStatusCode.NotFound, "TripNotFound");
        await AssertRefusedAsync(detail, HttpStatusCode.NotFound, "TripNotFound");
    }

    [Fact]
    public async Task TwoPlannersPuttingTheSameShipmentOnDifferentTripsAtOnce_GetOneTrip_AndTheOtherIsRefused()
    {
        var day = (await OpenHorizonAsync(3800)).AddDays(1);
        var shipment = await ReleasedAsync(day);
        var trips = new List<string>();
        foreach (var vehicle in Vehicles)
        {
            trips.Add(await NumberOf(await CreateAsync(day, vehicle, null)));
        }

        var answers = await Task.WhenAll(trips.Select(trip => AddAsync(trip, shipment)));

        answers.Count(answer => answer.StatusCode == HttpStatusCode.OK).Should().Be(1);
        foreach (var refused in answers.Where(answer => answer.StatusCode != HttpStatusCode.OK))
        {
            await AssertRefusedAsync(refused, HttpStatusCode.Conflict, "ShipmentOnAnotherTrip");
        }
    }

    [Fact]
    public async Task PuttingShipmentsOnATrip_PublishesNothing_AndWithoutTheManageRoleIsRefused()
    {
        var day = (await OpenHorizonAsync(3900)).AddDays(1);
        var shipment = await ReleasedAsync(day);
        await TmsFixture.SettleOutboxAsync(_cancellationToken);
        var before = tms.Published.Count();
        var trip = await NumberOf(await CreateAsync(day, Truck, null, shipment));
        using var client = tms.CreateTmsClient(tms.IssueToken([]));

        using var refused = await client.PostAsJsonAsync(new Uri($"/v1/trips/{trip}/shipments", UriKind.Relative), new { shipmentNumbers = (string[])[shipment] }, _cancellationToken);

        refused.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        await TmsFixture.SettleOutboxAsync(_cancellationToken);
        tms.Published.Count().Should().Be(before, "a Draft raises no event, and a Shipment on a Draft stays Released");
    }

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

    private Task<HttpResponseMessage> AddAsync(string trip, params string[] shipments) =>
        tms.TmsUi.PostAsJsonAsync(new Uri($"/v1/trips/{trip}/shipments", UriKind.Relative), new { shipmentNumbers = shipments }, _cancellationToken);

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

    private async Task<JsonObject> GetShipmentAsync(string number) => (await tms.GetShipmentAsync(number, _cancellationToken)).AsObject();

    private async Task<IReadOnlyList<JsonObject>> ListTripsAsync()
    {
        using var response = await tms.TmsUi.GetAsync(new Uri("/v1/trips", UriKind.Relative), _cancellationToken);
        return [.. JsonNode.Parse(await response.Content.ReadAsStringAsync(_cancellationToken))!.AsArray().Select(item => item!.AsObject())];
    }

    private async Task<IReadOnlyList<JsonObject>> ListToPutOnTripsAsync(DateOnly? day)
    {
        using var response = await tms.TmsUi.GetAsync(new Uri($"/v1/shipments/to-put-on-trips{(day is { } value ? $"?day={value:yyyy-MM-dd}" : string.Empty)}", UriKind.Relative), _cancellationToken);
        var body = await response.Content.ReadAsStringAsync(_cancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK, body);
        return [.. JsonNode.Parse(body)!.AsArray().Select(item => item!.AsObject())];
    }

    private static IEnumerable<string> Numbers(IEnumerable<JsonObject> list) => list.Select(item => item["shipmentNumber"]!.GetValue<string>());

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
