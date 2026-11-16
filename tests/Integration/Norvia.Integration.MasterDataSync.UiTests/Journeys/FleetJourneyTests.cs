using System.Globalization;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace Norvia.Integration.MasterDataSync.UiTests.Journeys;

/// <summary>
/// Finance makes a Vehicle or a Driver inactive in the ERP (E3). The TMS only stores that: nothing is edited, and a Trip that already uses it is flagged Needs a Vehicle or Needs a Driver.
/// The Trip needs a Vehicle and a Driver whose Roster covers the day, so the tests use the seed's Van VEH-0002 and Joost Bakker DRV-0002 and put each back as it was found.
/// </summary>
public sealed class FleetJourneyTests(MasterDataSyncUiFixture sync) : JourneyPage(sync)
{
    private const string Van = "VEH-0002";
    private const string Joost = "DRV-0002";

    [Fact]
    public Task Finance_DeactivatesAVehicle_TheTripUsingItIsFlaggedInTheTms_AndActivatingItClearsTheFlag() =>
        DeactivateAsync("vehicles", "Vehicle", Van, "VehicleCopies", "Needs a Vehicle");

    [Fact]
    public Task Finance_DeactivatesADriver_TheTripUsingThemIsFlaggedInTheTms_AndActivatingThemClearsTheFlag() =>
        DeactivateAsync("drivers", "Driver", Joost, "DriverCopies", "Needs a Driver");

    private async Task DeactivateAsync(string list, string kind, string id, string copies, string flag)
    {
        var (day, trip) = await Sync.PlanAsync(Van, Joost, Ct);
        var tms = await OpenTmsAsync("/capacity/days");
        var dayRow = tms.GetByRole(AriaRole.Table, new() { Name = "Open days" }).GetByRole(AriaRole.Row)
            .Filter(new() { HasText = day.ToString("ddd d MMM", CultureInfo.InvariantCulture) });
        await Expect(dayRow).ToBeVisibleAsync();
        await Expect(dayRow).Not.ToContainTextAsync(trip);

        var inactive = await SetActiveAsync(list, kind, id, active: false);
        try
        {
            await Sync.WaitForReplicaAsync(copies, id, inactive, Ct);
            await tms.ReloadAsync();
            await Expect(dayRow).ToContainTextAsync($"{trip} · {flag}");
        }
        finally
        {
            var active = await SetActiveAsync(list, kind, id, active: true);
            await Sync.WaitForReplicaAsync(copies, id, active, CancellationToken.None);
        }

        // The day is still open, and the Trip is no longer flagged on it.
        await tms.ReloadAsync();
        await Expect(dayRow).ToBeVisibleAsync();
        await Expect(dayRow).Not.ToContainTextAsync(trip);
    }
}
