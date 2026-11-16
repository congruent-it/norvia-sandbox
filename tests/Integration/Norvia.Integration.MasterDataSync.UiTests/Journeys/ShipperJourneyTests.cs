using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace Norvia.Integration.MasterDataSync.UiTests.Journeys;

/// <summary>Finance creates a Shipper in the ERP (E3), and the TMS knows it: an order for the Shipper shows its name.</summary>
public sealed class ShipperJourneyTests(MasterDataSyncUiFixture sync) : JourneyPage(sync)
{
    [Fact]
    public async Task Finance_AddsAShipper_ItIsListedInTheErp_AndAnOrderForItShowsItsNameInTheTms()
    {
        var shipper = NewId("SHP");
        var name = $"Journey Shipper {shipper}";

        var revision = await CreateShipperAsync(shipper, name);

        revision.Should().Be(1);
        await OpenErpAsync("/");
        await Expect(Page.GetByRole(AriaRole.Row).Filter(new() { HasText = shipper })).ToContainTextAsync(name);
        await Sync.WaitForReplicaAsync("ShipperCopies", shipper, 1, Ct);
        var order = await SubmitRatedOrderAsync(shipper);
        var tms = await OpenTmsAsync($"/transport-orders/{order}");
        await Expect(tms.GetByRole(AriaRole.Definition).Filter(new() { HasText = name })).ToBeVisibleAsync();
    }
}
