using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Norvia.Tms.Api.IntegrationTests.Planning;
using Norvia.Tms.Api.IntegrationTests.TransportOrders;
using static Norvia.Tms.Api.IntegrationTests.Capacity.CapacityCalls;

namespace Norvia.Tms.Api.IntegrationTests.Trips;

/// <summary>
/// Taking a Shipment off a Trip (P11) at the TMS's boundary: its Stops leave the Trip, it is Planned again so the Planner can release it again in P3,
/// and ShipmentTakenOffTrip is published under the Planner's correlation ID, which a Dispatched Trip's next version shares. Collected and In progress
/// need what only P12 and the Tracking events of a Carrier can make, so the handlers' unit tests cover them with that state set up. Each test moves
/// the clock to a stretch of days of its own.
/// </summary>
[Trait("AppHost", "Own4")]
public sealed class TripTakeOffTests(TripsTmsFixture tms) : IClassFixture<TripsTmsFixture>, IDisposable
{
    private const string Truck = "VEH-0002";
    private const string Joost = "DRV-0002";

    private static readonly TimeZoneInfo Amsterdam = TimeZoneInfo.FindSystemTimeZoneById("Europe/Amsterdam");

    private readonly CancellationToken _cancellationToken = TestContext.Current.CancellationToken;
    private readonly DateOnly _realToday = Today(DateTimeOffset.UtcNow);

    public void Dispose() => tms.ResetClock();

    [Fact]
    public async Task OnADispatchedTrip_TheShipmentIsPlannedAgain_ShipmentTakenOffTripIsPublished_AndVersion2SharesItsCorrelationIdWithoutTheStops()
    {
        var (trip, _, first) = await DispatchedTripAsync(7000);
        var second = await ReleasedAsync(DayOf(await GetTripDetailAsync(trip)), collectFrom: 10);
        (await AddShipmentAsync(trip, second)).Dispose();

        using var takenOff = await TakeOffAsync(trip, first, "The Consignee can't take the goods until next week.");

        takenOff.StatusCode.Should().Be(HttpStatusCode.OK, await takenOff.Content.ReadAsStringAsync(_cancellationToken));
        var correlationId = CorrelationIdOf(takenOff);
        JsonNode.Parse(await takenOff.Content.ReadAsStringAsync(_cancellationToken))!["version"]!.GetValue<int>().Should().Be(3);
        (await ShipmentStatusAsync(first)).Should().Be("Planned");
        var message = (await tms.PublishedAsync(correlationId, "ShipmentTakenOffTrip", _cancellationToken)).Should().ContainSingle().Subject;
        var body = JsonNode.Parse(message.Body.ToString())!;
        var results = CanonicalModelSchemas.ValidateShipmentTakenOffTrip(body);
        results.IsValid.Should().BeTrue(CanonicalModelSchemas.Errors(results));
        (body["shipmentNumber"]!.GetValue<string>(), body["tripNumber"]!.GetValue<string>(), body["version"]!.GetValue<int>(), body["reason"]!.GetValue<string>())
            .Should().Be((first, trip, 3, "The Consignee can't take the goods until next week."));
        message.ApplicationProperties["tripNumber"].Should().Be(trip);
        var version = JsonNode.Parse((await tms.PublishedAsync(correlationId, "TripDispatched", _cancellationToken)).Should().ContainSingle().Subject.Body.ToString())!;
        version["version"]!.GetValue<int>().Should().Be(3);
        version["stops"]!.AsArray().Select(stop => $"{stop!["kind"]} {stop["shipmentNumber"]}").Should().Equal($"Collection {second}", $"Delivery {second}");
        VersionsOf(await GetTripDetailAsync(trip)).Should().Equal("1 Sending", "2 Sending", "3 Sending");
    }

    [Fact]
    public async Task ATakenOffShipment_CanBeReleasedAgainInP3_AndPutOnAnotherTrip()
    {
        var (trip, _, shipment) = await DispatchedTripAsync(7100);
        var day = DayOf(await GetTripDetailAsync(trip));
        (await TakeOffAsync(trip, shipment, "Next week")).Dispose();
        (await GetTripDetailAsync(trip))["stops"]!.AsArray().Should().BeEmpty();

        var released = await tms.ReleasedAsync(shipment, _cancellationToken);

        released["status"]!.GetValue<string>().Should().Be("Released");
        using var another = await CreateAsync(day, "VEH-0001", "DRV-0001", "11:00", "02:00:00", shipment);
        (await NumberOf(another)).Should().NotBe(trip);
        (await ShipmentStatusAsync(shipment)).Should().Be("Released");
    }

    [Fact]
    public async Task OnADraft_TheShipmentIsPlannedAgain_AndShipmentTakenOffTripHasNoVersion()
    {
        var day = (await OpenHorizonAsync(7200)).AddDays(1);
        var shipment = await ReleasedAsync(day);
        var trip = await NumberOf(await CreateAsync(day, Truck, Joost, "09:00", "04:00:00", shipment));

        using var takenOff = await TakeOffAsync(trip, shipment, "Wrong day");

        takenOff.StatusCode.Should().Be(HttpStatusCode.OK, await takenOff.Content.ReadAsStringAsync(_cancellationToken));
        (await ShipmentStatusAsync(shipment)).Should().Be("Planned");
        var body = JsonNode.Parse((await tms.PublishedAsync(CorrelationIdOf(takenOff), "ShipmentTakenOffTrip", _cancellationToken)).Should().ContainSingle().Subject.Body.ToString())!;
        body["version"].Should().BeNull();
        CanonicalModelSchemas.ValidateShipmentTakenOffTrip(body).IsValid.Should().BeTrue();
        await TmsFixture.SettleOutboxAsync(_cancellationToken);
        (await TripDispatchedCountAsync(trip)).Should().Be(0, "a Draft has no version to send");
    }

    [Fact]
    public async Task ARefusedTakeOff_ChangesNothing_AndPublishesNothing()
    {
        var (trip, _, shipment) = await DispatchedTripAsync(7300);
        var other = await ReleasedAsync(DayOf(await GetTripDetailAsync(trip)), collectFrom: 10);

        await AssertRefusedAsync(await TakeOffAsync(trip, other, "Why"), HttpStatusCode.Conflict, "ShipmentNotOnTrip");
        await AssertRefusedAsync(await TakeOffAsync(trip, "SH-9999999", "Why"), HttpStatusCode.NotFound, "ShipmentNotFound");
        await AssertRefusedAsync(await TakeOffAsync("TR-9999999", shipment, "Why"), HttpStatusCode.NotFound, "TripNotFound");
        using var withoutReason = await TakeOffAsync(trip, shipment, " ");

        withoutReason.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ShipmentStatusAsync(shipment)).Should().Be("Dispatched");
        (await ShipmentStatusAsync(other)).Should().Be("Released");
        VersionsOf(await GetTripDetailAsync(trip)).Should().Equal("1 Sending");
        await TmsFixture.SettleOutboxAsync(_cancellationToken);
        tms.Published.Where(m => m.Subject == "ShipmentTakenOffTrip" && m.ApplicationProperties.TryGetValue("tripNumber", out var number) && number.Equals(trip)).Should().BeEmpty();
    }

    private Task<HttpResponseMessage> TakeOffAsync(string trip, string shipment, string reason) =>
        tms.TmsUi.PostAsJsonAsync(new Uri($"/v1/trips/{trip}/shipments/{shipment}/take-off", UriKind.Relative), new { reason }, _cancellationToken);

    private async Task<(string Trip, string CorrelationId, string Shipment)> DispatchedTripAsync(int daysAhead)
    {
        var day = (await OpenHorizonAsync(daysAhead)).AddDays(1);
        var shipment = await ReleasedAsync(day);
        var trip = await NumberOf(await CreateAsync(day, Truck, Joost, "09:00", "04:00:00", shipment));
        using var dispatched = await tms.TmsUi.PostAsync(new Uri($"/v1/trips/{trip}/dispatch", UriKind.Relative), content: null, _cancellationToken);
        var body = await dispatched.Content.ReadAsStringAsync(_cancellationToken);
        dispatched.StatusCode.Should().Be(HttpStatusCode.OK, body);
        var correlationId = JsonNode.Parse(body)!["correlationId"]!.GetValue<string>();
        await tms.PublishedAsync(correlationId, "TripDispatched", _cancellationToken);
        return (trip, correlationId, shipment);
    }

    private Task<HttpResponseMessage> AddShipmentAsync(string trip, string shipment) =>
        tms.TmsUi.PostAsJsonAsync(new Uri($"/v1/trips/{trip}/shipments", UriKind.Relative), new { shipmentNumbers = new[] { shipment } }, _cancellationToken);

    private static string CorrelationIdOf(HttpResponseMessage response) => response.Headers.GetValues("X-Correlation-ID").Single();

    private async Task<int> TripDispatchedCountAsync(string trip)
    {
        await TmsFixture.SettleOutboxAsync(_cancellationToken);
        return tms.Published.Count(m => m.Subject == "TripDispatched" && m.ApplicationProperties.TryGetValue("tripNumber", out var number) && number.Equals(trip));
    }

    private async Task<JsonNode> GetTripDetailAsync(string number)
    {
        using var response = await tms.TmsUi.GetAsync(new Uri($"/v1/trips/{number}", UriKind.Relative), _cancellationToken);
        var body = await response.Content.ReadAsStringAsync(_cancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK, body);
        return JsonNode.Parse(body)!;
    }

    private static DateOnly DayOf(JsonNode detail) => DateOnly.Parse(detail["trip"]!["day"]!.GetValue<string>(), System.Globalization.CultureInfo.InvariantCulture);

    private static IEnumerable<string> VersionsOf(JsonNode detail) =>
        detail["versions"]!.AsArray().Select(version => $"{version!["version"]} {version["state"]}");

    private async Task<string> ShipmentStatusAsync(string shipment)
    {
        using var response = await tms.TmsUi.GetAsync(new Uri($"/v1/shipments/{shipment}", UriKind.Relative), _cancellationToken);
        var body = await response.Content.ReadAsStringAsync(_cancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK, body);
        return JsonNode.Parse(body)!["status"]!.GetValue<string>();
    }

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
