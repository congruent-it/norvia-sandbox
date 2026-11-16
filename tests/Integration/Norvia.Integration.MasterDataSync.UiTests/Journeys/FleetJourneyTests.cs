using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace Norvia.Integration.MasterDataSync.UiTests.Journeys;

/// <summary>
/// Finance makes a Vehicle or a Driver inactive in the ERP (E3). The TMS only stores that: nothing is edited, and a Trip that already uses it is flagged Needs a Vehicle or Needs a Driver.
/// Each test puts the record back as it found it.
/// </summary>
public sealed class FleetJourneyTests(MasterDataSyncUiFixture sync) : JourneyPage(sync)
{
    private const string Van = "VEH-0002";
    private const string Joost = "DRV-0002";

    [Fact]
    public async Task Finance_DeactivatesAVehicle_TheTripUsingItIsFlaggedInTheTms_AndActivatingItClearsTheFlag()
    {
        var (day, trip) = await Sync.PlanAsync(Van, Joost, Ct);
        var tms = await OpenTmsAsync("/capacity/days");
        var flagged = tms.GetByRole(AriaRole.Table, new() { Name = "Open days" }).GetByRole(AriaRole.Row).Filter(new() { HasText = trip });
        await Expect(flagged).ToHaveCountAsync(0);

        var inactive = await SetActiveAsync("vehicles", "Vehicle", Van, active: false);
        try
        {
            await Sync.WaitForReplicaAsync("VehicleCopies", Van, inactive, Ct);
            await tms.ReloadAsync();
            await Expect(tms.GetByRole(AriaRole.Table, new() { Name = "Open days" }).GetByRole(AriaRole.Row).Filter(new() { HasText = trip }))
                .ToContainTextAsync("Needs a Vehicle");
        }
        finally
        {
            var active = await SetActiveAsync("vehicles", "Vehicle", Van, active: true);
            await Sync.WaitForReplicaAsync("VehicleCopies", Van, active, CancellationToken.None);
        }

        await tms.ReloadAsync();
        await Expect(tms.GetByRole(AriaRole.Table, new() { Name = "Open days" }).GetByRole(AriaRole.Row).Filter(new() { HasText = trip })).ToHaveCountAsync(0);
        day.Should().BeAfter(DateOnly.FromDateTime(DateTime.Today).AddDays(2));
    }

    [Fact]
    public async Task Finance_DeactivatesADriver_TheTripUsingThemIsFlaggedInTheTms_AndActivatingThemClearsTheFlag()
    {
        var (_, trip) = await Sync.PlanAsync(Van, Joost, Ct);
        var tms = await OpenTmsAsync("/capacity/days");

        var inactive = await SetActiveAsync("drivers", "Driver", Joost, active: false);
        try
        {
            await Sync.WaitForReplicaAsync("DriverCopies", Joost, inactive, Ct);
            await tms.ReloadAsync();
            await Expect(tms.GetByRole(AriaRole.Table, new() { Name = "Open days" }).GetByRole(AriaRole.Row).Filter(new() { HasText = trip }))
                .ToContainTextAsync("Needs a Driver");
        }
        finally
        {
            var active = await SetActiveAsync("drivers", "Driver", Joost, active: true);
            await Sync.WaitForReplicaAsync("DriverCopies", Joost, active, CancellationToken.None);
        }

        await tms.ReloadAsync();
        await Expect(tms.GetByRole(AriaRole.Table, new() { Name = "Open days" }).GetByRole(AriaRole.Row).Filter(new() { HasText = trip })).ToHaveCountAsync(0);
    }
}
