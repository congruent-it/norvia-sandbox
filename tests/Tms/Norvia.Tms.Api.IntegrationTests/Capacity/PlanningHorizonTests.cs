using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using static Norvia.Tms.Api.IntegrationTests.Capacity.CapacityCalls;

namespace Norvia.Tms.Api.IntegrationTests.Capacity;

/// <summary>
/// The horizon job (P10): at 00:00 Europe/Amsterdam it opens every missing day up to the end of the Planning horizon, fills
/// Shifts from the Rosters, catches up after downtime, changes nothing when it runs twice, and closes past days, which
/// are read-only. The tests share one TMS, so each moves the clock to a stretch of days of its own and reads only those.
/// The test seed's Rosters: Anouk de Vries works Monday to Saturday, Joost Bakker Monday to Friday, and the inactive Former
/// Driver never.
/// </summary>
[Trait("AppHost", "Own4")]
public sealed class PlanningHorizonTests(ControlledClockTmsFixture tms) : IClassFixture<ControlledClockTmsFixture>, IDisposable
{
    private readonly CancellationToken _cancellationToken = TestContext.Current.CancellationToken;
    private readonly DateOnly _realToday = Today(DateTimeOffset.UtcNow);

    public void Dispose() => tms.ResetClock();

    [Fact]
    public async Task AtMidnight_TheJobOpensTodayAndTheNext14Days_WithShiftsFromTheRosters_AndVehiclesAvailable()
    {
        var first = _realToday.AddDays(100);
        tms.SetClock(ShortlyAfterMidnightOf(first));

        var days = await tms.HorizonStartingAtAsync(first, _cancellationToken);

        days.Select(DateOf).Should().Equal(Enumerable.Range(0, 15).Select(first.AddDays));
        days.Should().OnlyContain(day => day["availableVehicles"]!.GetValue<int>() == 2, "the inactive Van is not available");
        foreach (var day in days)
        {
            var shifts = day["shifts"]!.AsArray().Select(shift => shift!.AsObject()).ToList();
            var expected = DateOf(day).DayOfWeek switch
            {
                DayOfWeek.Sunday => Array.Empty<string>(),
                DayOfWeek.Saturday => ["Anouk de Vries"],
                _ => ["Joost Bakker", "Anouk de Vries"],
            };
            shifts.Select(shift => shift["driverName"]!.GetValue<string>()).Should().BeEquivalentTo(expected, DateOf(day).ToString(System.Globalization.CultureInfo.InvariantCulture));
            shifts.Should().OnlyContain(shift => !shift["editedByHand"]!.GetValue<bool>());
        }

        var weekday = days.First(day => DateOf(day).DayOfWeek == DayOfWeek.Wednesday)["shifts"]!.AsArray();
        weekday.Select(shift => (shift!["driverName"]!.GetValue<string>(), shift["start"]!.GetValue<string>(), shift["end"]!.GetValue<string>()))
            .Should().Equal(("Anouk de Vries", "06:00:00", "14:30:00"), ("Joost Bakker", "08:00:00", "16:30:00"));
    }

    [Fact]
    public async Task AfterDowntime_TheNextRunOpensEveryMissingDay_AndTheDaysBeforeTodayLeaveTheOpenList()
    {
        var first = _realToday.AddDays(200);
        tms.SetClock(ShortlyAfterMidnightOf(first));
        await tms.HorizonStartingAtAsync(first, _cancellationToken);

        // The TMS was down for five midnights.
        var later = first.AddDays(5);
        tms.SetClock(ShortlyAfterMidnightOf(later));
        var days = await tms.HorizonStartingAtAsync(later, _cancellationToken);

        days.Select(DateOf).Should().Equal(Enumerable.Range(0, 15).Select(later.AddDays), "every missing day up to the end of the new horizon, once");
    }

    [Fact]
    public async Task ARunTheSameDayAgain_ChangesNothing_AndAShiftEditedByHandSurvivesARestart()
    {
        var first = _realToday.AddDays(300);
        tms.SetClock(ShortlyAfterMidnightOf(first));
        await tms.HorizonStartingAtAsync(first, _cancellationToken);
        using var edit = await tms.EditShiftAsync(first.AddDays(1), "DRV-0001", "05:00", "13:00", _cancellationToken);
        edit.StatusCode.Should().Be(HttpStatusCode.OK, await edit.Content.ReadAsStringAsync(_cancellationToken));
        var before = (await tms.HorizonStartingAtAsync(first, _cancellationToken)).Select(day => day.ToJsonString()).ToList();

        // Restarting the TMS runs the job again for the same day, which must open and change nothing.
        await tms.RestartAsync(_cancellationToken);
        await tms.TmsLogAsync(log => log.Text.Contains("Planning horizon job ran", StringComparison.Ordinal) && log.Text.Contains("opened 0 days, closed 0", StringComparison.Ordinal), _cancellationToken);

        var after = (await tms.HorizonStartingAtAsync(first, _cancellationToken)).Select(day => day.ToJsonString()).ToList();
        after.Should().Equal(before);
        var edited = JsonNode.Parse(after[1])!["shifts"]!.AsArray().Single(shift => shift!["driverName"]!.GetValue<string>() == "Anouk de Vries")!;
        edited["start"]!.GetValue<string>().Should().Be("05:00:00");
        edited["editedByHand"]!.GetValue<bool>().Should().BeTrue();
    }

    [Fact]
    public async Task ADayBeforeToday_IsClosedAndRejectsChanges_WhileTodayAndLaterDaysTakeThem()
    {
        var first = _realToday.AddDays(400);
        tms.SetClock(ShortlyAfterMidnightOf(first));
        await tms.HorizonStartingAtAsync(first, _cancellationToken);
        var today = first.AddDays(2);
        tms.SetClock(ShortlyAfterMidnightOf(today));
        await tms.HorizonStartingAtAsync(today, _cancellationToken);

        using var closed = await tms.EditShiftAsync(first, "DRV-0001", "05:00", "13:00", _cancellationToken);
        using var open = await tms.EditShiftAsync(today, "DRV-0001", "05:00", "13:00", _cancellationToken);
        using var beyond = await tms.EditShiftAsync(today.AddDays(40), "DRV-0001", "05:00", "13:00", _cancellationToken);
        using var unknownDriver = await tms.EditShiftAsync(today, "DRV-9999", "05:00", "13:00", _cancellationToken);
        using var backwards = await tms.EditShiftAsync(today, "DRV-0001", "13:00", "05:00", _cancellationToken);

        closed.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await Problem(closed))["code"]!.GetValue<string>().Should().Be("DayClosed");
        open.StatusCode.Should().Be(HttpStatusCode.OK);
        beyond.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await Problem(beyond))["code"]!.GetValue<string>().Should().Be("PlanningDayNotFound");
        unknownDriver.StatusCode.Should().Be(HttpStatusCode.NotFound);
        backwards.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var days = await tms.ListOpenDaysAsync(_cancellationToken);
        days.Select(DateOf).Should().NotContain(first, "a closed day leaves the open list");
        days.Single(day => DateOf(day) == today)["shifts"]!.AsArray().Should().Contain(shift => shift!["editedByHand"]!.GetValue<bool>());
    }

    [Fact]
    public async Task EditingAShift_WithoutTheManageRole_IsRefused()
    {
        using var client = tms.CreateTmsClient(tms.IssueToken([]));

        using var response = await client.PutAsJsonAsync(
            new Uri($"/v1/capacity/days/{_realToday:yyyy-MM-dd}/shifts/DRV-0001", UriKind.Relative), new { start = "05:00", end = "13:00" }, _cancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    private async Task<JsonObject> Problem(HttpResponseMessage response) =>
        JsonNode.Parse(await response.Content.ReadAsStringAsync(_cancellationToken))!.AsObject();
}
