using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Norvia.Tms.Api.IntegrationTests.Planning;
using Norvia.Tms.Api.IntegrationTests.TransportOrders;
using static Norvia.Tms.Api.IntegrationTests.Capacity.CapacityCalls;

namespace Norvia.Tms.Api.IntegrationTests.Trips;

/// <summary>
/// Amending a Dispatched Trip (P11) at the TMS's boundary: the Planner's edits work on a Dispatched Trip, each one that changes it raises the version
/// and publishes exactly one TripDispatched with the whole Trip under a new correlation ID, an added Shipment becomes Dispatched, and a refused Amend
/// leaves the Trip, its version and its Shipments as they were. TripUnderWay needs a Trip that is In progress, which only P12 can make, so the
/// handlers' unit tests cover it with that state set up. Each test moves the clock to a stretch of days of its own.
/// </summary>
[Trait("AppHost", "Own4")]
public sealed class TripAmendTests(TripsTmsFixture tms) : IClassFixture<TripsTmsFixture>, IDisposable
{
    private const string Truck = "VEH-0002";
    private const string Joost = "DRV-0002";

    private static readonly TimeZoneInfo Amsterdam = TimeZoneInfo.FindSystemTimeZoneById("Europe/Amsterdam");

    private readonly CancellationToken _cancellationToken = TestContext.Current.CancellationToken;
    private readonly DateOnly _realToday = Today(DateTimeOffset.UtcNow);

    public void Dispose() => tms.ResetClock();

    [Fact]
    public async Task ChangingThePlannedDuration_RaisesVersion2_AndPublishesOneTripDispatchedWithTheWholeTripUnderTheAmendsCorrelationId()
    {
        var (trip, dispatchCorrelationId, _) = await DispatchedTripAsync(6000);

        using var changed = await ChangeAsync(trip, "09:00", "05:00:00");

        changed.StatusCode.Should().Be(HttpStatusCode.NoContent, await changed.Content.ReadAsStringAsync(_cancellationToken));
        var amendId = CorrelationIdOf(changed);
        amendId.Should().NotBe(dispatchCorrelationId);
        var message = (await tms.PublishedAsync(amendId, "TripDispatched", _cancellationToken)).Should().ContainSingle().Subject;
        var body = JsonNode.Parse(message.Body.ToString())!;
        var results = CanonicalModelSchemas.ValidateTripDispatched(body);
        results.IsValid.Should().BeTrue(CanonicalModelSchemas.Errors(results));
        body["version"]!.GetValue<int>().Should().Be(2);
        body["tripNumber"]!.GetValue<string>().Should().Be(trip);
        body["stops"]!.AsArray().Should().HaveCount(2);
        (await TripDispatchedCountAsync(trip)).Should().Be(2, "the Dispatch and the Amend");
        VersionsOf(await GetTripDetailAsync(trip)).Should().Equal("1 Sending", "2 Sending");
        (await GetTripDetailAsync(trip))["trip"]!["status"]!.GetValue<string>().Should().Be("Dispatched");
    }

    [Fact]
    public async Task AddingAShipmentToADispatchedTrip_MakesItDispatched_AndSendsTheWholeTripAsTheNextVersion()
    {
        var (trip, _, first) = await DispatchedTripAsync(6100);
        var second = await ReleasedAsync(DayOf(await GetTripDetailAsync(trip)), collectFrom: 10);

        using var added = await AddShipmentAsync(trip, second);

        added.StatusCode.Should().Be(HttpStatusCode.OK, await added.Content.ReadAsStringAsync(_cancellationToken));
        (await ShipmentStatusAsync(second)).Should().Be("Dispatched");
        var amendId = CorrelationIdOf(added);
        var body = JsonNode.Parse((await tms.PublishedAsync(amendId, "TripDispatched", _cancellationToken)).Should().ContainSingle().Subject.Body.ToString())!;
        body["version"]!.GetValue<int>().Should().Be(2);
        body["stops"]!.AsArray().Should().HaveCount(4, "the whole Trip goes out, the Stops of the first Shipment too");
        body["stops"]!.ToJsonString().Should().Contain(first).And.Contain(second);
    }

    [Fact]
    public async Task ReorderingTheStopsOfADispatchedTrip_SendsTheNewOrderAsTheNextVersion()
    {
        var (trip, _, first) = await DispatchedTripAsync(6200);
        var day = DayOf(await GetTripDetailAsync(trip));
        var second = await ReleasedAsync(day, collectFrom: 10);
        (await AddShipmentAsync(trip, second)).Dispose();

        using var reordered = await ReorderAsync(trip, (second, "Collection"), (first, "Collection"), (first, "Delivery"), (second, "Delivery"));

        reordered.StatusCode.Should().Be(HttpStatusCode.NoContent, await reordered.Content.ReadAsStringAsync(_cancellationToken));
        var body = JsonNode.Parse((await tms.PublishedAsync(CorrelationIdOf(reordered), "TripDispatched", _cancellationToken)).Should().ContainSingle().Subject.Body.ToString())!;
        body["version"]!.GetValue<int>().Should().Be(3);
        body["stops"]!.AsArray().Select(stop => $"{stop!["kind"]} {stop["shipmentNumber"]}").Should().Equal(
            $"Collection {second}", $"Collection {first}", $"Delivery {first}", $"Delivery {second}");
        VersionsOf(await GetTripDetailAsync(trip)).Should().Equal("1 Sending", "2 Sending", "3 Sending");
    }

    [Fact]
    public async Task ARefusedAmend_LeavesTheTripItsVersionAndItsShipmentsUnchanged_AndPublishesNothing()
    {
        var (trip, _, _) = await DispatchedTripAsync(6300);
        var day = DayOf(await GetTripDetailAsync(trip));
        var otherDay = await ReleasedAsync(day.AddDays(1));

        await AssertRefusedAsync(await AddShipmentAsync(trip, otherDay), HttpStatusCode.Conflict, "CollectionDayMismatch");
        await AssertRefusedAsync(await ChangeAsync(trip, "09:00", null), HttpStatusCode.Conflict, "TripIncomplete");

        (await ShipmentStatusAsync(otherDay)).Should().Be("Released");
        VersionsOf(await GetTripDetailAsync(trip)).Should().Equal("1 Sending");
        await TmsFixture.SettleOutboxAsync(_cancellationToken);
        (await TripDispatchedCountAsync(trip)).Should().Be(1);
    }

    [Fact]
    public async Task AnAmendThatChangesNothing_RaisesNoVersion()
    {
        var (trip, _, _) = await DispatchedTripAsync(6400);

        using var same = await ChangeAsync(trip, "09:00", "04:00:00");

        same.StatusCode.Should().Be(HttpStatusCode.NoContent);
        await TmsFixture.SettleOutboxAsync(_cancellationToken);
        VersionsOf(await GetTripDetailAsync(trip)).Should().Equal("1 Sending");
        (await TripDispatchedCountAsync(trip)).Should().Be(1);
    }

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

    private Task<HttpResponseMessage> ChangeAsync(string trip, string? start, string? duration) =>
        tms.TmsUi.PutAsJsonAsync(new Uri($"/v1/trips/{trip}", UriKind.Relative), new { vehicleId = Truck, driverId = Joost, plannedStart = start, plannedDrivingDuration = duration }, _cancellationToken);

    private Task<HttpResponseMessage> AddShipmentAsync(string trip, string shipment) =>
        tms.TmsUi.PostAsJsonAsync(new Uri($"/v1/trips/{trip}/shipments", UriKind.Relative), new { shipmentNumbers = new[] { shipment } }, _cancellationToken);

    private Task<HttpResponseMessage> ReorderAsync(string trip, params (string Shipment, string Kind)[] order) =>
        tms.TmsUi.PutAsJsonAsync(
            new Uri($"/v1/trips/{trip}/stops", UriKind.Relative), new { stops = order.Select(stop => new { shipmentNumber = stop.Shipment, kind = stop.Kind }) }, _cancellationToken);

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
