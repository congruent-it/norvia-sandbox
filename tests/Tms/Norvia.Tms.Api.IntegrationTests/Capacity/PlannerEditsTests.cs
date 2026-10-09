using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using static Norvia.Tms.Api.IntegrationTests.Capacity.CapacityCalls;

namespace Norvia.Tms.Api.IntegrationTests.Capacity;

/// <summary>
/// The Planner's edits (P10): Absences and Off the road days, also beyond the Planning horizon, Shift edits and Roster
/// changes. The tests share one TMS and move its clock to a stretch of days of their own. The test seed's Rosters: Anouk de
/// Vries works Monday to Saturday, Joost Bakker Monday to Friday. Only the Roster test changes a Roster, and it uses Joost.
/// </summary>
[Trait("AppHost", "Own4")]
public sealed class PlannerEditsTests(ControlledClockTmsFixture tms) : IClassFixture<ControlledClockTmsFixture>, IDisposable
{
    private readonly CancellationToken _cancellationToken = TestContext.Current.CancellationToken;
    private readonly DateOnly _realToday = Today(DateTimeOffset.UtcNow);

    public void Dispose() => tms.ResetClock();

    [Fact]
    public async Task AnAbsenceBeyondTheHorizon_IsAccepted_AndTakesEffectWhenTheDayOpens()
    {
        var first = Monday(_realToday.AddDays(500));
        tms.SetClock(ShortlyAfterMidnightOf(first));
        await tms.HorizonStartingAtAsync(first, _cancellationToken);
        var beyond = first.AddDays(16); // A Wednesday, which Anouk works.

        using var recorded = await tms.RecordPeriodAsync("/v1/capacity/absences", "DRV-0001", beyond, beyond, "Leave", _cancellationToken);
        recorded.StatusCode.Should().Be(HttpStatusCode.OK, await recorded.Content.ReadAsStringAsync(_cancellationToken));
        (await tms.ListOpenDaysAsync(_cancellationToken)).Select(DateOf).Should().NotContain(beyond);

        tms.SetClock(ShortlyAfterMidnightOf(first.AddDays(7)));
        var days = await tms.HorizonStartingAtAsync(first.AddDays(7), _cancellationToken);

        var opened = days.Single(day => DateOf(day) == beyond);
        DriversOn(opened).Should().Equal(["Joost Bakker"], "Anouk's Absence took effect when the day opened");
    }

    [Fact]
    public async Task AnAbsenceOnAnOpenDay_TakesTheDriversShiftAway_AndRemovingItGivesTheRostersShiftBack()
    {
        var first = Monday(_realToday.AddDays(600));
        tms.SetClock(ShortlyAfterMidnightOf(first));
        await tms.HorizonStartingAtAsync(first, _cancellationToken);
        var day = first.AddDays(2);

        using var recorded = await tms.RecordPeriodAsync("/v1/capacity/absences", "DRV-0001", day, day, "Sickness", _cancellationToken);
        recorded.StatusCode.Should().Be(HttpStatusCode.OK);
        DriversOn(await DayAsync(day)).Should().Equal("Joost Bakker");

        var absences = await ListAsync("/v1/capacity/absences");
        var absence = absences.Single(item => DateOnly.Parse(item["date"]!.GetValue<string>(), System.Globalization.CultureInfo.InvariantCulture) == day);
        using var removed = await tms.TmsUi.DeleteAsync(new Uri($"/v1/capacity/absences/{absence["id"]!.GetValue<string>()}", UriKind.Relative), _cancellationToken);
        removed.StatusCode.Should().Be(HttpStatusCode.NoContent);

        DriversOn(await DayAsync(day)).Should().BeEquivalentTo("Joost Bakker", "Anouk de Vries");
    }

    [Fact]
    public async Task AnOffTheRoadDay_TakesTheVehicleOutOfThatDayOnly()
    {
        var first = Monday(_realToday.AddDays(700));
        tms.SetClock(ShortlyAfterMidnightOf(first));
        await tms.HorizonStartingAtAsync(first, _cancellationToken);
        var day = first.AddDays(3);

        using var recorded = await tms.RecordPeriodAsync("/v1/capacity/off-the-road-days", "VEH-0001", day, day, "Maintenance", _cancellationToken);

        recorded.StatusCode.Should().Be(HttpStatusCode.OK, await recorded.Content.ReadAsStringAsync(_cancellationToken));
        (await DayAsync(day))["availableVehicles"]!.GetValue<int>().Should().Be(1);
        (await DayAsync(day.AddDays(1)))["availableVehicles"]!.GetValue<int>().Should().Be(2);
        var offTheRoad = await ListAsync("/v1/capacity/off-the-road-days");
        offTheRoad.Should().Contain(item => item["registration"] != null && item["reason"]!.GetValue<string>() == "Maintenance");
    }

    [Fact]
    public async Task ARosterChange_RewritesTheOpenDays_ExceptOnesEditedByHandOrWithAnAbsence()
    {
        var first = Monday(_realToday.AddDays(800));
        tms.SetClock(ShortlyAfterMidnightOf(first));
        await tms.HorizonStartingAtAsync(first, _cancellationToken);
        var edited = first.AddDays(1);
        var absent = first.AddDays(2);
        using var edit = await tms.EditShiftAsync(edited, "DRV-0002", "05:00", "13:00", _cancellationToken);
        using var absence = await tms.RecordPeriodAsync("/v1/capacity/absences", "DRV-0002", absent, absent, "Training", _cancellationToken);

        using var changed = await tms.ChangeRosterAsync("DRV-0002", [DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday], "09:00", "17:00", _cancellationToken);

        changed.StatusCode.Should().Be(HttpStatusCode.OK, await changed.Content.ReadAsStringAsync(_cancellationToken));
        ShiftOf(await DayAsync(first), "Joost Bakker")!["start"]!.GetValue<string>().Should().Be("09:00:00");
        var editedShift = ShiftOf(await DayAsync(edited), "Joost Bakker")!;
        editedShift["start"]!.GetValue<string>().Should().Be("05:00:00", "a Shift edited by hand survives the Roster change");
        editedShift["editedByHand"]!.GetValue<bool>().Should().BeTrue();
        ShiftOf(await DayAsync(absent), "Joost Bakker").Should().BeNull("the Driver has an Absence that day");
        ShiftOf(await DayAsync(first.AddDays(4)), "Joost Bakker").Should().BeNull("Friday is a day off in the new Roster");
        var rosters = await ListAsync("/v1/capacity/rosters");
        rosters.Single(roster => roster["driverId"]!.GetValue<string>() == "DRV-0002")["weekdays"]!.AsArray().Should().HaveCount(4);
    }

    [Fact]
    public async Task EditsToClosedDays_AreRejectedWithAStableCode()
    {
        var first = Monday(_realToday.AddDays(900));
        tms.SetClock(ShortlyAfterMidnightOf(first));
        await tms.HorizonStartingAtAsync(first, _cancellationToken);
        var today = first.AddDays(2);
        tms.SetClock(ShortlyAfterMidnightOf(today));
        await tms.HorizonStartingAtAsync(today, _cancellationToken);

        using var absence = await tms.RecordPeriodAsync("/v1/capacity/absences", "DRV-0001", first, first, "Leave", _cancellationToken);
        using var offTheRoad = await tms.RecordPeriodAsync("/v1/capacity/off-the-road-days", "VEH-0001", first, first, "Repair", _cancellationToken);
        using var backwards = await tms.RecordPeriodAsync("/v1/capacity/absences", "DRV-0001", today.AddDays(2), today, "Leave", _cancellationToken);
        using var badReason = await tms.RecordPeriodAsync("/v1/capacity/absences", "DRV-0001", today, today, "Holiday", _cancellationToken);

        absence.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await Problem(absence))["code"]!.GetValue<string>().Should().Be("DayClosed");
        offTheRoad.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await Problem(offTheRoad))["code"]!.GetValue<string>().Should().Be("DayClosed");
        backwards.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await Problem(backwards))["code"]!.GetValue<string>().Should().Be("PeriodInvalid");
        badReason.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task RecordingAnAbsence_WithoutTheManageRole_IsRefused()
    {
        using var client = tms.CreateTmsClient(tms.IssueToken([]));

        using var response = await client.PostAsJsonAsync(
            new Uri("/v1/capacity/absences", UriKind.Relative), new { id = "DRV-0001", from = "2026-12-01", to = "2026-12-01", reason = "Leave" }, _cancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    private static DateOnly Monday(DateOnly day) => day.AddDays(((int)DayOfWeek.Monday - (int)day.DayOfWeek + 7) % 7);

    private static List<string> DriversOn(JsonObject day) =>
        [.. day["shifts"]!.AsArray().Select(shift => shift!["driverName"]!.GetValue<string>())];

    private static JsonObject? ShiftOf(JsonObject day, string driverName) =>
        day["shifts"]!.AsArray().Select(shift => shift!.AsObject()).SingleOrDefault(shift => shift["driverName"]!.GetValue<string>() == driverName);

    private async Task<JsonObject> DayAsync(DateOnly date) => (await tms.ListOpenDaysAsync(_cancellationToken)).Single(day => DateOf(day) == date);

    private async Task<IReadOnlyList<JsonObject>> ListAsync(string path)
    {
        using var response = await tms.TmsUi.GetAsync(new Uri(path, UriKind.Relative), _cancellationToken);
        var body = await response.Content.ReadAsStringAsync(_cancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK, body);
        return [.. JsonNode.Parse(body)!.AsArray().Select(item => item!.AsObject())];
    }

    private async Task<JsonObject> Problem(HttpResponseMessage response) =>
        JsonNode.Parse(await response.Content.ReadAsStringAsync(_cancellationToken))!.AsObject();
}
