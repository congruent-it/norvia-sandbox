using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace Norvia.Integration.MasterDataSync.UiTests.Journeys;

/// <summary>
/// Finance fixes a Shipper's Rate Card in the ERP (E3). The TMS replaces its copy, so a Transport Order rated afterwards uses the new version and one rated before keeps its Freight Charge.
/// </summary>
public sealed class RateCardJourneyTests(MasterDataSyncUiFixture sync) : JourneyPage(sync)
{
    [Fact]
    public async Task Finance_FixesTheRateCard_TheTmsShowsTheNewVersion_AndTheOrderRatedBeforeKeepsItsFreightCharge()
    {
        var shipper = NewId("SHP");
        var card = NewId("RC");
        await CreateShipperAsync(shipper, "Rate card journey Shipper");
        await CreateRateCardAsync(card, shipper, [CardVersions.Version(1, "2020-01-01", null, 65m)]);
        await Sync.WaitForReplicaAsync("ShipperCopies", shipper, 1, Ct);
        await Sync.WaitForReplicaAsync("RateCardCopies", card, 1, Ct);
        var before = await SubmitRatedOrderAsync(shipper);

        // The fix: version 1 stops at the end of 2024, and version 2, dearer, takes over.
        var revision = await ReplaceRateCardVersionsAsync(card, [CardVersions.Version(1, "2020-01-01", "2024-12-31", 65m), CardVersions.Version(2, "2025-01-01", null, 130m)]);
        revision.Should().Be(2);
        await Sync.WaitForReplicaAsync("RateCardCopies", card, revision, Ct);
        var after = await SubmitRatedOrderAsync(shipper);

        var rated = await OpenTmsAsync($"/transport-orders/{after}");
        await Expect(rated.GetByText($"{card}, version 2")).ToBeVisibleAsync();
        await Expect(rated.GetByRole(AriaRole.Definition).Filter(new() { HasText = "EUR 130.00" }).First).ToBeVisibleAsync();

        var earlier = await OpenTmsAsync($"/transport-orders/{before}");
        await Expect(earlier.GetByText($"{card}, version 1")).ToBeVisibleAsync();
        await Expect(earlier.GetByRole(AriaRole.Definition).Filter(new() { HasText = "EUR 65.00" }).First).ToBeVisibleAsync();
    }
}
