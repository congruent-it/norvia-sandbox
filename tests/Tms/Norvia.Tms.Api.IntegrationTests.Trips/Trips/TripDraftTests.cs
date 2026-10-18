using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using static Norvia.Tms.TestSupport.Capacity.CapacityCalls;

namespace Norvia.Tms.Api.IntegrationTests.Trips;

/// <summary>
/// Draft Trips (P11) at the TMS's boundary: created for an open day with or without a Vehicle and Driver, refused by P10's
/// availability rules and by the one-Trip-a-day rule, changed, and discarded. The test seed's Vehicles are the van VEH-0001, the
/// rigid truck VEH-0002 and the tractor VEH-0003; Anouk (DRV-0001) holds B, C and CE, Joost (DRV-0002) B and C. Each test moves the
/// clock to a stretch of days of its own.
/// </summary>
public sealed class TripDraftTests(TripsTmsFixture tms) : IClassFixture<TripsTmsFixture>, IDisposable
{
    private const string Van = "VEH-0001";
    private const string Truck = "VEH-0002";
    private const string Anouk = "DRV-0001";
    private const string Joost = "DRV-0002";

    private readonly CancellationToken _cancellationToken = TestContext.Current.CancellationToken;
    private readonly DateOnly _realToday = Today(DateTimeOffset.UtcNow);

    public void Dispose() => tms.ResetClock();

    [Fact]
    public async Task ADraft_CanBeCreatedEmptyOrWithAVehicleAndDriver_AndTheTripsViewListsIt()
    {
        var first = await OpenHorizonAsync(1000);
        var day = first.AddDays(1);

        var empty = await CreateAsync(day, null, null);
        var full = await CreateAsync(day.AddDays(1), Van, Anouk);

        empty.StatusCode.Should().Be(HttpStatusCode.Created, await empty.Content.ReadAsStringAsync(_cancellationToken));
        full.StatusCode.Should().Be(HttpStatusCode.Created);
        var emptyNumber = await NumberOf(empty);
        var fullNumber = await NumberOf(full);
        var trips = await ListAsync();
        var emptyTrip = trips.Single(trip => trip["number"]!.GetValue<string>() == emptyNumber);
        emptyTrip["status"]!.GetValue<string>().Should().Be("Draft");
        emptyTrip["vehicleId"].Should().BeNull();
        var fullTrip = trips.Single(trip => trip["number"]!.GetValue<string>() == fullNumber);
        fullTrip["vehicleRegistration"]!.GetValue<string>().Should().Be("VN-101-A");
        fullTrip["driverName"]!.GetValue<string>().Should().Be("Anouk de Vries");
    }

    [Fact]
    public async Task EachOfP10sCodes_IsReturnedWhenItsRuleIsBroken_AndTheTripIsUnchanged()
    {
        var first = await OpenHorizonAsync(1100);
        var day = first.AddDays(1);
        var trip = await NumberOf(await CreateAsync(day, null, Anouk));
        using var offTheRoad = await tms.RecordPeriodAsync("/v1/capacity/off-the-road-days", Truck, day, day, "Repair", _cancellationToken);
        using var absence = await tms.RecordPeriodAsync("/v1/capacity/absences", Joost, day, day, "Leave", _cancellationToken);

        using var vehicleOff = await ChangeAsync(trip, Truck, Anouk);
        using var driverAbsent = await ChangeAsync(trip, Van, Joost);
        using var licence = await ChangeAsync(trip, TripsTmsFixture.TractorId, null);
        using var closed = await CreateAsync(_realToday.AddDays(-3), null, null);

        await AssertRefusedAsync(vehicleOff, HttpStatusCode.Conflict, "VehicleNotAvailable");
        await AssertRefusedAsync(driverAbsent, HttpStatusCode.Conflict, "DriverNotAvailable");
        await AssertRefusedAsync(closed, HttpStatusCode.Conflict, "DayNotOpen");
        licence.StatusCode.Should().Be(HttpStatusCode.NoContent, "Anouk holds CE");
        var unchanged = (await ListAsync()).Single(item => item["number"]!.GetValue<string>() == trip);
        unchanged["vehicleId"]!.GetValue<string>().Should().Be(TripsTmsFixture.TractorId, "only the refused changes left it alone");
        unchanged["driverId"].Should().BeNull();
    }

    [Fact]
    public async Task LicenceNotHeld_IsReturned_WhenTheDriversLicenceDoesNotCoverTheVehicle()
    {
        var day = (await OpenHorizonAsync(1200)).AddDays(1);

        using var response = await CreateAsync(day, TripsTmsFixture.TractorId, Joost);

        await AssertRefusedAsync(response, HttpStatusCode.Conflict, "LicenceNotHeld");
        (await ListAsync()).Should().NotContain(trip => trip["day"]!.GetValue<string>() == $"{day:yyyy-MM-dd}");
    }

    [Fact]
    public async Task ASecondTripForTheSameVehicleOrDriver_IsRefused_AlsoWhenTheFirstIsADraft()
    {
        var day = (await OpenHorizonAsync(1300)).AddDays(1);
        using var first = await CreateAsync(day, Van, Anouk);

        using var sameVehicle = await CreateAsync(day, Van, Joost);
        using var sameDriver = await CreateAsync(day, Truck, Anouk);
        using var otherDay = await CreateAsync(day.AddDays(1), Van, Anouk);

        first.StatusCode.Should().Be(HttpStatusCode.Created);
        await AssertRefusedAsync(sameVehicle, HttpStatusCode.Conflict, "VehicleNotAvailable");
        await AssertRefusedAsync(sameDriver, HttpStatusCode.Conflict, "DriverNotAvailable");
        otherDay.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task DiscardingADraft_GivesItsVehicleAndDriverBack_AndAnUnknownTripIsNotFound()
    {
        var day = (await OpenHorizonAsync(1400)).AddDays(1);
        var number = await NumberOf(await CreateAsync(day, Van, Anouk));

        using var discarded = await tms.TmsUi.DeleteAsync(new Uri($"/v1/trips/{number}", UriKind.Relative), _cancellationToken);
        using var again = await CreateAsync(day, Van, Anouk);
        using var unknown = await tms.TmsUi.DeleteAsync(new Uri("/v1/trips/TR-9999999", UriKind.Relative), _cancellationToken);

        discarded.StatusCode.Should().Be(HttpStatusCode.NoContent);
        again.StatusCode.Should().Be(HttpStatusCode.Created);
        (await ListAsync()).Should().NotContain(trip => trip["number"]!.GetValue<string>() == number);
        await AssertRefusedAsync(unknown, HttpStatusCode.NotFound, "TripNotFound");
    }

    [Fact]
    public async Task AStartInsideTheShiftAndADurationThatFits_AreSaved_AndTheTripsViewListsThem()
    {
        var day = (await OpenHorizonAsync(1800)).AddDays(1);

        using var created = await CreateAsync(day, Van, Anouk, start: "06:00", duration: "06:30:00");

        created.StatusCode.Should().Be(HttpStatusCode.Created, await created.Content.ReadAsStringAsync(_cancellationToken));
        var number = await NumberOf(created);
        var trip = (await ListAsync()).Single(item => item["number"]!.GetValue<string>() == number);
        trip["plannedStart"]!.GetValue<string>().Should().Be("06:00:00");
        trip["plannedDrivingDuration"]!.GetValue<string>().Should().Be("06:30:00");
    }

    [Theory]
    [InlineData("05:59", 1900)]
    [InlineData("14:30", 1950)]
    public async Task OutsideShift_IsReturnedForAStartOutsideTheDriversShift_AndTheTripIsUnchanged(string start, int daysAhead)
    {
        var day = (await OpenHorizonAsync(daysAhead)).AddDays(1);
        var number = await NumberOf(await CreateAsync(day, Van, Anouk, start: "07:00", duration: "04:00:00"));

        using var response = await ChangeAsync(number, Van, Anouk, start, "05:00:00");

        await AssertRefusedAsync(response, HttpStatusCode.Conflict, "OutsideShift");
        var unchanged = (await ListAsync()).Single(item => item["number"]!.GetValue<string>() == number);
        unchanged["plannedStart"]!.GetValue<string>().Should().Be("07:00:00");
        unchanged["plannedDrivingDuration"]!.GetValue<string>().Should().Be("04:00:00");
    }

    [Fact]
    public async Task DrivingHoursExceeded_IsReturnedWhenTheDurationDoesNotFitTheBudget()
    {
        var day = (await OpenHorizonAsync(2000)).AddDays(1);

        using var response = await CreateAsync(day, Van, Anouk, start: "07:00", duration: "10:30:00");

        await AssertRefusedAsync(response, HttpStatusCode.Conflict, "DrivingHoursExceeded");
        (await ListAsync()).Should().NotContain(trip => trip["day"]!.GetValue<string>() == $"{day:yyyy-MM-dd}");
    }

    [Fact]
    public async Task ADurationOfTenHours_FitsWhileADailyExtensionIsLeft()
    {
        var day = (await OpenHorizonAsync(2150)).AddDays(1);

        using var response = await CreateAsync(day, Van, Anouk, start: "07:00", duration: "10:00:00");

        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync(_cancellationToken));
    }

    [Theory]
    [InlineData("00:00:00")]
    [InlineData("-01:00:00")]
    [InlineData("1.01:00:00")]
    public async Task ADurationOutsideADay_IsABadRequest(string duration)
    {
        var day = (await OpenHorizonAsync(2100)).AddDays(1);

        using var response = await CreateAsync(day, Van, Anouk, duration: duration);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task ADraftsDuration_UsesUpTheDriversHours_AndDiscardingTheDraftGivesThemBack()
    {
        // The daily limit is 9 h, or 10 h on at most two days of a week. Tuesday and Wednesday use both extensions.
        var monday = await OpenHorizonAsync(2200);
        var tuesday = await NumberOf(await CreateAsync(monday.AddDays(1), Van, Anouk, duration: "10:00:00"));
        using var wednesday = await CreateAsync(monday.AddDays(2), Van, Anouk, duration: "10:00:00");
        wednesday.StatusCode.Should().Be(HttpStatusCode.Created);

        using var refused = await CreateAsync(monday.AddDays(3), Van, Anouk, duration: "10:00:00");
        using var discarded = await tms.TmsUi.DeleteAsync(new Uri($"/v1/trips/{tuesday}", UriKind.Relative), _cancellationToken);
        using var again = await CreateAsync(monday.AddDays(3), Van, Anouk, duration: "10:00:00");

        await AssertRefusedAsync(refused, HttpStatusCode.Conflict, "DrivingHoursExceeded");
        discarded.StatusCode.Should().Be(HttpStatusCode.NoContent);
        again.StatusCode.Should().Be(HttpStatusCode.Created, await again.Content.ReadAsStringAsync(_cancellationToken));
    }

    [Fact]
    public async Task TwoRequestsAskingAtOnceForTheSameVehicle_CreateOneTrip_AndTheOtherIsRefusedWithAStableCode()
    {
        var day = (await OpenHorizonAsync(1600)).AddDays(1);

        var answers = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => CreateAsync(day, Van, null)));

        answers.Count(answer => answer.StatusCode == HttpStatusCode.Created).Should().Be(1);
        foreach (var refused in answers.Where(answer => answer.StatusCode != HttpStatusCode.Created))
        {
            await AssertRefusedAsync(refused, HttpStatusCode.Conflict, "VehicleNotAvailable");
        }

        (await ListAsync()).Count(trip => trip["day"]!.GetValue<string>() == $"{day:yyyy-MM-dd}").Should().Be(1);
    }

    [Fact]
    public async Task TheSameRequestSentAgain_IsAnsweredWithTheDraftItCreated()
    {
        var day = (await OpenHorizonAsync(1700)).AddDays(1);

        using var first = await CreateAsync(day, Van, Anouk, key: "trip-retry-1");
        using var again = await CreateAsync(day, Van, Anouk, key: "trip-retry-1");

        again.StatusCode.Should().Be(HttpStatusCode.Created);
        (await NumberOf(again)).Should().Be(await NumberOf(first));
        (await ListAsync()).Count(trip => trip["day"]!.GetValue<string>() == $"{day:yyyy-MM-dd}").Should().Be(1);
    }

    [Fact]
    public async Task CreatingADraft_WithoutAnIdempotencyKey_IsABadRequest()
    {
        using var response = await tms.TmsUi.PostAsJsonAsync(new Uri("/v1/trips", UriKind.Relative), new { day = "2026-12-01" }, _cancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task CreatingChangingOrDiscardingADraft_PublishesNothing()
    {
        var day = (await OpenHorizonAsync(1500)).AddDays(1);
        var number = await NumberOf(await CreateAsync(day, null, null));
        using var changed = await ChangeAsync(number, Van, Anouk);
        using var discarded = await tms.TmsUi.DeleteAsync(new Uri($"/v1/trips/{number}", UriKind.Relative), _cancellationToken);

        changed.StatusCode.Should().Be(HttpStatusCode.NoContent);
        discarded.StatusCode.Should().Be(HttpStatusCode.NoContent);
        await TmsFixture.SettleOutboxAsync(_cancellationToken);
        tms.Published.Should().BeEmpty("a Draft raises no event, and nothing else in this fixture publishes");
    }

    [Fact]
    public async Task CreatingADraft_WithoutTheManageRole_IsRefused()
    {
        using var client = tms.CreateTmsClient(tms.IssueToken([]));

        using var response = await client.PostAsJsonAsync(new Uri("/v1/trips", UriKind.Relative), new { day = "2026-12-01" }, _cancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    private async Task<DateOnly> OpenHorizonAsync(int daysAhead)
    {
        var first = _realToday.AddDays(daysAhead);
        first = first.AddDays(((int)DayOfWeek.Monday - (int)first.DayOfWeek + 7) % 7);
        tms.SetClock(ShortlyAfterMidnightOf(first));
        await tms.HorizonStartingAtAsync(first, _cancellationToken);
        return first;
    }

    private async Task<HttpResponseMessage> CreateAsync(
        DateOnly day, string? vehicleId, string? driverId, string? key = null, string? start = null, string? duration = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri("/v1/trips", UriKind.Relative))
        {
            Content = JsonContent.Create(new { day = $"{day:yyyy-MM-dd}", vehicleId, driverId, plannedStart = start, plannedDrivingDuration = duration }),
        };
        request.Headers.Add("Idempotency-Key", key ?? Guid.NewGuid().ToString());
        return await tms.TmsUi.SendAsync(request, _cancellationToken);
    }

    private Task<HttpResponseMessage> ChangeAsync(string number, string? vehicleId, string? driverId, string? start = null, string? duration = null) =>
        tms.TmsUi.PutAsJsonAsync(
            new Uri($"/v1/trips/{number}", UriKind.Relative), new { vehicleId, driverId, plannedStart = start, plannedDrivingDuration = duration }, _cancellationToken);

    private async Task<string> NumberOf(HttpResponseMessage created) =>
        JsonNode.Parse(await created.Content.ReadAsStringAsync(_cancellationToken))!["number"]!.GetValue<string>();

    private async Task<IReadOnlyList<JsonObject>> ListAsync()
    {
        using var response = await tms.TmsUi.GetAsync(new Uri("/v1/trips", UriKind.Relative), _cancellationToken);
        var body = await response.Content.ReadAsStringAsync(_cancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK, body);
        return [.. JsonNode.Parse(body)!.AsArray().Select(item => item!.AsObject())];
    }

    private async Task AssertRefusedAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        var body = await response.Content.ReadAsStringAsync(_cancellationToken);
        response.StatusCode.Should().Be(status, body);
        var problem = JsonNode.Parse(body)!.AsObject();
        problem["code"]!.GetValue<string>().Should().Be(code);
        problem["detail"]!.GetValue<string>().Should().NotBeNullOrWhiteSpace("the refusal has a readable message");
    }
}
