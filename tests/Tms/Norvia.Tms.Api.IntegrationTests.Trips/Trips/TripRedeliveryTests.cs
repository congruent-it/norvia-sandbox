using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using static Norvia.Tms.TestSupport.Capacity.CapacityCalls;

namespace Norvia.Tms.Api.IntegrationTests.Trips;

/// <summary>
/// Re-delivery on a later Trip (P12) at the TMS's boundary: a Shipment the Driver collected but couldn't deliver is Awaiting re-delivery, so Shipments to
/// put on Trips lists it marked Re-delivery. It goes on a Trip for another day with only its delivery Stop, the load rules still apply, Dispatch clears
/// the flag while the Shipment stays Collected, and taking it off sets the flag again. The clock is controlled, so each test sets the days it works on.
/// </summary>
public sealed class TripRedeliveryTests(TripsTmsFixture tms) : IClassFixture<TripsTmsFixture>, IDisposable
{
    private const string Truck = "VEH-0002";
    private const string Joost = "DRV-0002";
    private const string Van = "VEH-0001";

    private static readonly TimeZoneInfo Amsterdam = TimeZoneInfo.FindSystemTimeZoneById("Europe/Amsterdam");

    private readonly CancellationToken _cancellationToken = TestContext.Current.CancellationToken;
    private readonly DateOnly _realToday = Today(DateTimeOffset.UtcNow);

    public void Dispose() => tms.ResetClock();

    [Fact]
    public async Task ARedelivery_IsListedMarkedRedelivery_GoesOnATripForAnotherDayWithOnlyItsDeliveryStop_DispatchClearsTheFlag_AndTakingItOffSetsItAgain()
    {
        var shipment = await AwaitingRedeliveryAsync(8400);
        var listed = (await ToPutOnTripsAsync()).Single(item => item!["shipmentNumber"]!.GetValue<string>() == shipment)!;
        listed["redelivery"]!.GetValue<bool>().Should().BeTrue();

        var day = (await OpenHorizonAsync(8500)).AddDays(1);
        using var created = await CreateAsync(day, Truck, Joost, "09:00", "04:00:00", shipment);
        var trip = await NumberOf(created);

        var detail = await GetTripAsync(trip);
        detail["stops"]!.AsArray().Select(stop => $"{stop!["kind"]} {stop["shipmentNumber"]}").Should().Equal($"Delivery {shipment}");
        (await ToPutOnTripsAsync()).Select(item => item!["shipmentNumber"]!.GetValue<string>()).Should().NotContain(shipment);
        (await ShipmentAsync(shipment))["awaitingRedelivery"]!.GetValue<bool>().Should().BeTrue();

        using var dispatched = await tms.TmsUi.PostAsync(new Uri($"/v1/trips/{trip}/dispatch", UriKind.Relative), content: null, _cancellationToken);
        var dispatchedBody = await dispatched.Content.ReadAsStringAsync(_cancellationToken);
        dispatched.StatusCode.Should().Be(HttpStatusCode.OK, dispatchedBody);
        var version = JsonNode.Parse((await tms.PublishedAsync(JsonNode.Parse(dispatchedBody)!["correlationId"]!.GetValue<string>(), "TripDispatched", _cancellationToken)).Should().ContainSingle().Subject.Body.ToString())!;
        version["stops"]!.AsArray().Select(stop => $"{stop!["kind"]} {stop["shipmentNumber"]}").Should().Equal($"Delivery {shipment}");
        var cleared = await ShipmentAsync(shipment);
        cleared["status"]!.GetValue<string>().Should().Be("Collected");
        cleared["awaitingRedelivery"]!.GetValue<bool>().Should().BeFalse();

        using var takenOff = await tms.TmsUi.PostAsJsonAsync(new Uri($"/v1/trips/{trip}/shipments/{shipment}/take-off", UriKind.Relative), new { reason = "Consignee closed" }, _cancellationToken);
        takenOff.StatusCode.Should().Be(HttpStatusCode.OK, await takenOff.Content.ReadAsStringAsync(_cancellationToken));
        var again = await ShipmentAsync(shipment);
        again["status"]!.GetValue<string>().Should().Be("Collected");
        again["awaitingRedelivery"]!.GetValue<bool>().Should().BeTrue();
        (await GetTripAsync(trip))["stops"]!.AsArray().Should().BeEmpty();
        (await ToPutOnTripsAsync()).Select(item => item!["shipmentNumber"]!.GetValue<string>()).Should().Contain(shipment);
    }

    [Fact]
    public async Task ARedelivery_IsRefusedWhenItBreaksTheLoadRules_AndStaysAwaitingRedelivery()
    {
        var shipment = await AwaitingRedeliveryAsync(8300, pallets: 5);
        var day = (await OpenHorizonAsync(8350)).AddDays(1);

        using var refused = await CreateAsync(day, Van, Joost, "09:00", "04:00:00", shipment);

        var body = await refused.Content.ReadAsStringAsync(_cancellationToken);
        refused.StatusCode.Should().Be(HttpStatusCode.Conflict, body);
        body.Should().Contain("PalletPlacesExceeded");
        (await ShipmentAsync(shipment))["awaitingRedelivery"]!.GetValue<bool>().Should().BeTrue();
    }

    // Collected on a Trip of its own, whose Driver couldn't deliver it, and ended: the Shipment is Awaiting re-delivery.
    private async Task<string> AwaitingRedeliveryAsync(int daysAhead, int pallets = 2)
    {
        var (trip, day, shipments) = await StartedTripAsync(daysAhead, pallets);
        var now = ShortlyAfterMidnightOf(day);
        await RecordAsync(Report("Collected", trip, shipments[0], now.AddMinutes(-30)));
        await RecordAsync(Report("DeliveryFailed", trip, shipments[0], now.AddMinutes(-20)));
        using var ended = await EndAsync(trip, Joost);
        ended.StatusCode.Should().Be(HttpStatusCode.OK, await ended.Content.ReadAsStringAsync(_cancellationToken));
        (await ShipmentAsync(shipments[0]))["awaitingRedelivery"]!.GetValue<bool>().Should().BeTrue();
        return shipments[0];
    }

    private async Task<JsonArray> ToPutOnTripsAsync()
    {
        using var response = await tms.TmsUi.GetAsync(new Uri("/v1/shipments/to-put-on-trips", UriKind.Relative), _cancellationToken);
        var body = await response.Content.ReadAsStringAsync(_cancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK, body);
        return JsonNode.Parse(body)!.AsArray();
    }

    private Task<HttpResponseMessage> EndAsync(string trip, string? driverId)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, new Uri($"/v1/trips/{trip}/end", UriKind.Relative))
        {
            Content = JsonContent.Create(new JsonObject { ["driverId"] = driverId }),
        };
        request.Headers.Add("X-Correlation-ID", TmsCalls.NewCorrelationId());
        return tms.TripEnd.SendAsync(request, _cancellationToken);
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

    private async Task<(string Trip, DateOnly Day, string[] Shipments)> StartedTripAsync(int daysAhead, int pallets)
    {
        var (trip, day, shipments) = await DispatchedTripAsync(daysAhead, pallets);
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

    private async Task<(string Trip, DateOnly Day, string[] Shipments)> DispatchedTripAsync(int daysAhead, int pallets)
    {
        var day = (await OpenHorizonAsync(daysAhead)).AddDays(1);
        string[] shipments = [await ReleasedAsync(day, pallets)];
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

    private async Task<DateOnly> OpenHorizonAsync(int daysAhead)
    {
        var first = _realToday.AddDays(daysAhead);
        first = first.AddDays(((int)DayOfWeek.Monday - (int)first.DayOfWeek + 7) % 7);
        tms.SetClock(ShortlyAfterMidnightOf(first));
        await tms.HorizonStartingAtAsync(first, _cancellationToken);
        return first;
    }

    // A Released Own fleet Shipment that collects from 08:00 on the day, the given number of pallets of 100 kg.
    private async Task<string> ReleasedAsync(DateOnly day, int pallets)
    {
        var from = new DateTimeOffset(day.ToDateTime(new TimeOnly(8, 0)), Amsterdam.GetUtcOffset(day.ToDateTime(new TimeOnly(8, 0))));
        var booking = Bookings.Valid(TmsCalls.NewReference()).CollectedFrom(from);
        booking["goodsLines"] = new JsonArray { new JsonObject { ["packageType"] = "pallet", ["quantity"] = pallets, ["weightKg"] = 100m, ["dimensionsCm"] = null } };
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

    private async Task<JsonNode> GetTripAsync(string number)
    {
        using var response = await tms.TmsUi.GetAsync(new Uri($"/v1/trips/{number}", UriKind.Relative), _cancellationToken);
        var body = await response.Content.ReadAsStringAsync(_cancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK, body);
        return JsonNode.Parse(body)!;
    }

    private async Task<string> NumberOf(HttpResponseMessage created)
    {
        var body = await created.Content.ReadAsStringAsync(_cancellationToken);
        created.StatusCode.Should().Be(HttpStatusCode.Created, body);
        return JsonNode.Parse(body)!["number"]!.GetValue<string>();
    }
}
