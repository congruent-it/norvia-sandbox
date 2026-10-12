using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Norvia.Tms.Api.IntegrationTests.Planning;
using Norvia.Tms.Api.IntegrationTests.TransportOrders;
using static Norvia.Tms.Api.IntegrationTests.Capacity.CapacityCalls;

namespace Norvia.Tms.Api.IntegrationTests.Trips;

/// <summary>
/// The Needs a Driver and Needs a Vehicle flags (P11) at the TMS's boundary, with the controlled clock P10's tests use: an Absence, an Off the road day
/// and a Roster change are accepted and flag the Trip, removing the cause clears the flag, the Capacity view lists the flagged Trips, and nothing about
/// the Trip, its Shipments or its versions changes. Each test moves the clock to a stretch of days of its own.
/// </summary>
[Trait("AppHost", "Own4")]
public sealed class TripFlagTests(TripsTmsFixture tms) : IClassFixture<TripsTmsFixture>, IDisposable
{
    private const string Truck = "VEH-0002";
    private const string Joost = "DRV-0002";

    private static readonly TimeZoneInfo Amsterdam = TimeZoneInfo.FindSystemTimeZoneById("Europe/Amsterdam");

    private readonly CancellationToken _cancellationToken = TestContext.Current.CancellationToken;
    private readonly DateOnly _realToday = Today(DateTimeOffset.UtcNow);

    public void Dispose() => tms.ResetClock();

    [Fact]
    public async Task AnAbsence_FlagsTheDraftNeedsADriver_TheCapacityViewListsIt_AndRemovingTheAbsenceClearsTheFlag()
    {
        var (day, trip, _) = await DraftAsync(4500);

        using var absence = await tms.RecordPeriodAsync("/v1/capacity/absences", Joost, day, day, "Sickness", _cancellationToken);

        absence.StatusCode.Should().Be(HttpStatusCode.OK, await absence.Content.ReadAsStringAsync(_cancellationToken));
        (await FlagsOfAsync(trip)).Should().Be((true, false));
        (await FlaggedTripsOfAsync(day)).Should().Equal((trip, true, false));
        await RemoveAsync("/v1/capacity/absences", day);
        (await FlagsOfAsync(trip)).Should().Be((false, false));
        (await FlaggedTripsOfAsync(day)).Should().BeEmpty();
    }

    [Fact]
    public async Task AFlaggedDraft_CantBeDispatched_AndNothingChanges()
    {
        var (day, trip, shipment) = await DraftAsync(4900);
        using var absence = await tms.RecordPeriodAsync("/v1/capacity/absences", Joost, day, day, "Sickness", _cancellationToken);
        absence.StatusCode.Should().Be(HttpStatusCode.OK);
        (await FlagsOfAsync(trip)).Should().Be((true, false));

        using var refused = await tms.TmsUi.PostAsync(new Uri($"/v1/trips/{trip}/dispatch", UriKind.Relative), content: null, _cancellationToken);

        refused.StatusCode.Should().Be(HttpStatusCode.Conflict);
        JsonNode.Parse(await refused.Content.ReadAsStringAsync(_cancellationToken))!["code"]!.GetValue<string>().Should().Be("DriverNotAvailable");
        (await GetTripAsync(trip))["trip"]!["status"]!.GetValue<string>().Should().Be("Draft");
        (await ShipmentStatusAsync(shipment)).Should().Be("Released");
        (await FlagsOfAsync(trip)).Should().Be((true, false), "the refusal doesn't clear or change the flag");
    }

    [Fact]
    public async Task AnOffTheRoadDay_FlagsTheDraftNeedsAVehicle_AndRemovingItClearsTheFlag()
    {
        var (day, trip, _) = await DraftAsync(4600);

        using var offTheRoad = await tms.RecordPeriodAsync("/v1/capacity/off-the-road-days", Truck, day, day, "Repair", _cancellationToken);

        offTheRoad.StatusCode.Should().Be(HttpStatusCode.OK, await offTheRoad.Content.ReadAsStringAsync(_cancellationToken));
        (await FlagsOfAsync(trip)).Should().Be((false, true));
        (await FlaggedTripsOfAsync(day)).Should().Equal((trip, false, true));
        await RemoveAsync("/v1/capacity/off-the-road-days", day);
        (await FlagsOfAsync(trip)).Should().Be((false, false));
    }

    [Fact]
    public async Task ARosterChangeThatNoLongerCoversTheDay_FlagsTheTripNeedsADriver_AndRestoringTheRosterClearsTheFlag()
    {
        var (day, trip, _) = await DraftAsync(4700);
        var other = Enum.GetValues<DayOfWeek>().Where(weekday => weekday != day.DayOfWeek).ToList();

        using var changed = await tms.ChangeRosterAsync(Joost, other, "08:00", "16:00", _cancellationToken);

        changed.StatusCode.Should().Be(HttpStatusCode.OK, await changed.Content.ReadAsStringAsync(_cancellationToken));
        (await FlagsOfAsync(trip)).Should().Be((true, false));
        using var restored = await tms.ChangeRosterAsync(Joost, Enum.GetValues<DayOfWeek>(), "08:00", "16:00", _cancellationToken);
        restored.StatusCode.Should().Be(HttpStatusCode.OK);
        (await FlagsOfAsync(trip)).Should().Be((false, false));
    }

    [Fact]
    public async Task AFlaggedDispatchedTrip_StaysWithItsDriver_ChangesNothing_AndPublishesNothingNew()
    {
        var (day, trip, shipment) = await DraftAsync(4800);
        using var dispatched = await tms.TmsUi.PostAsync(new Uri($"/v1/trips/{trip}/dispatch", UriKind.Relative), content: null, _cancellationToken);
        dispatched.StatusCode.Should().Be(HttpStatusCode.OK, await dispatched.Content.ReadAsStringAsync(_cancellationToken));
        await TmsFixture.SettleOutboxAsync(_cancellationToken);
        var publishedBefore = tms.Published.Count();

        using var absence = await tms.RecordPeriodAsync("/v1/capacity/absences", Joost, day, day, "Sickness", _cancellationToken);

        absence.StatusCode.Should().Be(HttpStatusCode.OK);
        var detail = await GetTripAsync(trip);
        (detail["trip"]!["needsDriver"]!.GetValue<bool>(), detail["trip"]!["status"]!.GetValue<string>(), detail["trip"]!["driverId"]!.GetValue<string>())
            .Should().Be((true, "Dispatched", Joost));
        detail["versions"]!.AsArray().Should().ContainSingle();
        (await ShipmentStatusAsync(shipment)).Should().Be("Dispatched");
        await TmsFixture.SettleOutboxAsync(_cancellationToken);
        tms.Published.Count().Should().Be(publishedBefore);
    }

    private async Task<(DateOnly Day, string Trip, string Shipment)> DraftAsync(int daysAhead)
    {
        var first = _realToday.AddDays(daysAhead);
        first = first.AddDays(((int)DayOfWeek.Monday - (int)first.DayOfWeek + 7) % 7);
        tms.SetClock(ShortlyAfterMidnightOf(first));
        await tms.HorizonStartingAtAsync(first, _cancellationToken);
        var day = first.AddDays(1);
        var shipment = await ReleasedAsync(day);
        var trip = await NumberOf(await CreateAsync(day, shipment));
        return (day, trip, shipment);
    }

    private async Task<(bool Driver, bool Vehicle)> FlagsOfAsync(string trip)
    {
        var listed = JsonNode.Parse(await tms.TmsUi.GetStringAsync(new Uri("/v1/trips", UriKind.Relative), _cancellationToken))!.AsArray()
            .Single(item => item!["number"]!.GetValue<string>() == trip)!;
        var detail = (await GetTripAsync(trip))["trip"]!;
        (detail["needsDriver"]!.GetValue<bool>(), detail["needsVehicle"]!.GetValue<bool>())
            .Should().Be((listed["needsDriver"]!.GetValue<bool>(), listed["needsVehicle"]!.GetValue<bool>()), "the list and the detail say the same");
        return (listed["needsDriver"]!.GetValue<bool>(), listed["needsVehicle"]!.GetValue<bool>());
    }

    private async Task<IReadOnlyList<(string Number, bool NeedsDriver, bool NeedsVehicle)>> FlaggedTripsOfAsync(DateOnly day) =>
    [
        .. (await tms.ListOpenDaysAsync(_cancellationToken)).Single(item => DateOf(item) == day)["flaggedTrips"]!.AsArray()
            .Select(item => (item!["number"]!.GetValue<string>(), item["needsDriver"]!.GetValue<bool>(), item["needsVehicle"]!.GetValue<bool>())),
    ];

    private async Task RemoveAsync(string path, DateOnly day)
    {
        var records = JsonNode.Parse(await tms.TmsUi.GetStringAsync(new Uri(path, UriKind.Relative), _cancellationToken))!.AsArray();
        var record = records.Single(item => DateOnly.Parse(item!["date"]!.GetValue<string>(), System.Globalization.CultureInfo.InvariantCulture) == day);
        using var removed = await tms.TmsUi.DeleteAsync(new Uri($"{path}/{record!["id"]!.GetValue<string>()}", UriKind.Relative), _cancellationToken);
        removed.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    private async Task<JsonNode> GetTripAsync(string number)
    {
        using var response = await tms.TmsUi.GetAsync(new Uri($"/v1/trips/{number}", UriKind.Relative), _cancellationToken);
        var body = await response.Content.ReadAsStringAsync(_cancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK, body);
        return JsonNode.Parse(body)!;
    }

    private async Task<string> ShipmentStatusAsync(string shipment)
    {
        var body = await tms.TmsUi.GetStringAsync(new Uri($"/v1/shipments/{shipment}", UriKind.Relative), _cancellationToken);
        return JsonNode.Parse(body)!["status"]!.GetValue<string>();
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

    private async Task<HttpResponseMessage> CreateAsync(DateOnly day, string shipment)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri("/v1/trips", UriKind.Relative))
        {
            Content = JsonContent.Create(new
            {
                day = $"{day:yyyy-MM-dd}",
                vehicleId = Truck,
                driverId = Joost,
                plannedStart = "09:00",
                plannedDrivingDuration = "04:00:00",
                shipmentNumbers = new[] { shipment },
            }),
        };
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
        return await tms.TmsUi.SendAsync(request, _cancellationToken);
    }

    private async Task<string> NumberOf(HttpResponseMessage created)
    {
        using var _ = created;
        var body = await created.Content.ReadAsStringAsync(_cancellationToken);
        created.StatusCode.Should().Be(HttpStatusCode.Created, body);
        return JsonNode.Parse(body)!["number"]!.GetValue<string>();
    }
}
