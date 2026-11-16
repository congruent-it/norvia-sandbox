using Microsoft.Playwright;
using Norvia.Integration.MasterDataSync.IntegrationTests;
using static Microsoft.Playwright.Assertions;

namespace Norvia.Integration.MasterDataSync.UiTests.Journeys;

/// <summary>With the TMS refusing the replace command, the ERP edit still saves (the ERP never calls the TMS) and the Integration console shows the message the sync dead-lettered.</summary>
public sealed class FailureJourneyTests(MasterDataSyncUiFixture sync) : JourneyPage(sync)
{
    private const string Subscription = "master-data-events/master-data-sync";

    [Fact]
    public async Task WhenTheTmsRefusesTheCommand_TheErpEditStillSaves_AndTheConsoleShowsTheDeadLetter()
    {
        var shipper = NewId("SHP");
        await CreateShipperAsync(shipper, "Shipper");
        await Sync.WaitForReplicaAsync("ShipperCopies", shipper, 1, Ct);
        Sync.Proxy.Script($"/v1/shippers/{shipper}", Fault.Answer(400));

        var revision = await RenameShipperAsync(shipper, "Refused Shipper");

        revision.Should().Be(2);
        await OpenErpAsync("/");
        await Expect(Page.GetByRole(AriaRole.Row).Filter(new() { HasText = shipper })).ToContainTextAsync("Refused Shipper");
        var console = await Page.Context.NewPageAsync();
        await console.GotoAsync(new Uri(Sync.IntegrationConsole, $"/dead-letter-queues/{Subscription}").ToString());
        await Expect(console.GetByRole(AriaRole.Main)).ToContainTextAsync("TmsCallRefused");
        await Expect(console.GetByRole(AriaRole.Main)).ToContainTextAsync("The test scripted 400.");
        // The one call the worker made for the Shipper is the one the TMS refused: a client error isn't retried.
        Sync.Proxy.CallsTo($"/v1/shippers/{shipper}").Should().HaveCount(2, "the create went through, and the refused edit was sent once and not retried");
    }
}
