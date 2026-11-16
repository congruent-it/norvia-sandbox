using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace Norvia.Integration.MasterDataSync.UiTests.Journeys;

/// <summary>Finance's forms refuse what can't be right, with a message beside the form, and nothing is saved.</summary>
public sealed class ValidationJourneyTests(MasterDataSyncUiFixture sync) : JourneyPage(sync)
{
    [Fact]
    public async Task TheShipperForm_RefusesAnIdTheErpAlreadyHolds()
    {
        await OpenErpAsync("/shippers/new");
        await Page.GetByRole(AriaRole.Textbox, new() { Name = "ID", Exact = true }).FillAsync("SHP-0001");
        await Page.GetByRole(AriaRole.Textbox, new() { Name = "Name", Exact = true }).FillAsync("A second Brouwer");

        await ClickSaveAsync("Save Shipper");

        await Expect(Page.GetByRole(AriaRole.Alert)).ToContainTextAsync("There is already a Shipper with ID SHP-0001.");
        await Expect(Page.GetByRole(AriaRole.Status)).Not.ToContainTextAsync("Saved");
    }

    [Fact]
    public async Task TheVehicleForm_RefusesARegistrationAnotherVehicleHas()
    {
        await OpenErpAsync("/vehicles/new");
        await Page.GetByRole(AriaRole.Textbox, new() { Name = "ID", Exact = true }).FillAsync(NewId("VEH"));
        await Page.GetByRole(AriaRole.Textbox, new() { Name = "Registration", Exact = true }).FillAsync("VN-101-A");

        await ClickSaveAsync("Save Vehicle");

        await Expect(Page.GetByRole(AriaRole.Alert)).ToContainTextAsync("Another Vehicle already has the registration VN-101-A.");
    }

    [Fact]
    public async Task TheRateCardForm_RefusesVersionsThatOverlapInTime()
    {
        var shipper = NewId("SHP");
        await CreateShipperAsync(shipper, "Overlap journey Shipper");
        await FillNewRateCardAsync(NewId("RC"), shipper,
            [CardVersions.Version(1, "2020-01-01", "2025-06-30", 65m), CardVersions.Version(2, "2025-01-01", null, 130m)]);

        await ClickSaveAsync("Save Rate Card");

        await Expect(Page.GetByRole(AriaRole.Alert)).ToContainTextAsync("Versions 1 and 2 overlap in time; versions must not overlap.");
        await Expect(Page.GetByRole(AriaRole.Status)).Not.ToContainTextAsync("Saved");
    }
}
