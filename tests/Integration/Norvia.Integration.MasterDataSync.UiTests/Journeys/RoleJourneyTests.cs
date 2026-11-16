using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace Norvia.Integration.MasterDataSync.UiTests.Journeys;

/// <summary>
/// The ERP UI is Finance's: a user without the Finance role is refused and sees no master data, so there is nothing for them to edit. (The ERP has no read-only view.)
/// </summary>
public sealed class RoleJourneyTests(MasterDataSyncUiFixture sync) : JourneyPage(sync)
{
    [Fact]
    public async Task AUserWithoutTheFinanceRole_IsRefused_AndSeesNoMasterData()
    {
        var response = await Page.GotoAsync(new Uri(Sync.ErpUiOfAReader, "/").ToString());

        response!.Status.Should().Be(403);
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "You can't use the ERP", Level = 1 })).ToBeVisibleAsync();
        await Expect(Page.GetByRole(AriaRole.Main)).ToContainTextAsync("Finance");
        await Expect(Page.GetByRole(AriaRole.Row)).ToHaveCountAsync(0);
        await Expect(Page.GetByRole(AriaRole.Link, new() { Name = "New Shipper" })).ToHaveCountAsync(0);

        var form = await Page.GotoAsync(new Uri(Sync.ErpUiOfAReader, "/shippers/new").ToString());

        form!.Status.Should().Be(403);
        await Expect(Page.GetByRole(AriaRole.Button, new() { Name = "Save Shipper" })).ToHaveCountAsync(0);
    }
}
