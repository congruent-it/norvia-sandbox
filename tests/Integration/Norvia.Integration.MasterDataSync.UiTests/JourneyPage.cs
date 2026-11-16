using System.Globalization;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.Playwright;
using Norvia.Erp.TestSupport;
using static Microsoft.Playwright.Assertions;

namespace Norvia.Integration.MasterDataSync.UiTests;

/// <summary>
/// A fresh browser page per test, on the ERP UI as Finance, with pages of the TMS UI and of the Integration console in the same browser context for the part of the journey that
/// happens there. <see cref="TracedPage"/> keeps the trace and video of a failed test. A test makes the records it needs through the ERP's forms, with IDs of its own.
/// </summary>
public abstract partial class JourneyPage(MasterDataSyncUiFixture sync) : TracedPage(sync.Browser, sync.ErpUi)
{
    protected MasterDataSyncUiFixture Sync { get; } = sync;

    /// <summary>The TMS UI, in the same browser context as the ERP's.</summary>
    protected async Task<IPage> OpenTmsAsync(string path)
    {
        var tms = await Page.Context.NewPageAsync();
        await tms.GotoAsync(new Uri(Sync.TmsWeb, path).ToString());
        return tms;
    }

    /// <summary>A new ID in the shape of the estate's, unlike any other, short enough for the TMS's columns.</summary>
    protected static string NewId(string prefix) => $"{prefix}-U{Guid.NewGuid():N}"[..(prefix.Length + 10)];

    /// <summary>Opens an ERP page and waits until the browser is talking to it, so what a test types isn't lost to a page that hasn't started.</summary>
    protected async Task OpenErpAsync(string path)
    {
        await Page.GotoAsync(path);
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
    }

    protected Task ClickSaveAsync(string button) => Page.GetByRole(AriaRole.Button, new() { Name = button }).ClickAsync();

    /// <summary>Creates a Shipper in the ERP's form, and returns the Master-data revision the ERP says it saved.</summary>
    protected async Task<int> CreateShipperAsync(string id, string name)
    {
        await OpenErpAsync("/shippers/new");
        await Page.GetByRole(AriaRole.Textbox, new() { Name = "ID", Exact = true }).FillAsync(id);
        await Page.GetByRole(AriaRole.Textbox, new() { Name = "Name", Exact = true }).FillAsync(name);
        await ClickSaveAsync("Save Shipper");
        return await SavedNewAsync();
    }

    /// <summary>
    /// A new record's form saves it and moves on to the record's edit page, so that move is the answer. The record is new, so the revision it was saved at is 1.
    /// </summary>
    private async Task<int> SavedNewAsync()
    {
        await Expect(Page).ToHaveURLAsync(new Regex("/edit$"));
        return 1;
    }

    /// <summary>Creates a Rate Card of a Shipper in the ERP's form with the versions as JSON in the shape of the ERP API's body, and returns the revision the ERP says it saved.</summary>
    protected async Task<int> CreateRateCardAsync(string id, string shipperId, JsonArray versions)
    {
        await FillNewRateCardAsync(id, shipperId, versions);
        await ClickSaveAsync("Save Rate Card");
        return await SavedNewAsync();
    }

    /// <summary>Opens the new Rate Card form and fills it, without saving.</summary>
    protected async Task FillNewRateCardAsync(string id, string shipperId, JsonArray versions)
    {
        await OpenErpAsync("/rate-cards/new");
        await Page.GetByRole(AriaRole.Textbox, new() { Name = "ID", Exact = true }).FillAsync(id);
        await Page.GetByRole(AriaRole.Combobox, new() { Name = "Shipper" }).ClickAsync();
        await Page.GetByRole(AriaRole.Option, new() { Name = shipperId, Exact = false }).ClickAsync();
        await Page.GetByRole(AriaRole.Textbox, new() { Name = "Versions (JSON)", Exact = true }).FillAsync(versions.ToJsonString());
    }

    /// <summary>Renames a Shipper in its edit form, and returns the revision the ERP says it saved.</summary>
    protected async Task<int> RenameShipperAsync(string id, string name)
    {
        await OpenErpAsync($"/shippers/{id}/edit");
        await Page.GetByRole(AriaRole.Textbox, new() { Name = "Name", Exact = true }).FillAsync(name);
        await ClickSaveAsync("Save Shipper");
        return await SavedRevisionAsync();
    }

    /// <summary>Replaces the versions of a Rate Card in its edit form, and returns the revision the ERP says it saved.</summary>
    protected async Task<int> ReplaceRateCardVersionsAsync(string id, JsonArray versions)
    {
        await OpenErpAsync($"/rate-cards/{id}/edit");
        await Page.GetByRole(AriaRole.Textbox, new() { Name = "Versions (JSON)", Exact = true }).FillAsync(versions.ToJsonString());
        await ClickSaveAsync("Save Rate Card");
        return await SavedRevisionAsync();
    }

    /// <summary>Sets a Vehicle or a Driver active or inactive in its edit form, and returns the revision the ERP says it saved. <paramref name="list"/> is <c>vehicles</c> or <c>drivers</c>.</summary>
    protected async Task<int> SetActiveAsync(string list, string kind, string id, bool active)
    {
        await OpenErpAsync($"/{list}/{id}/edit");
        var box = Page.GetByRole(AriaRole.Checkbox, new() { Name = "Active", Exact = true });
        if (await box.GetAttributeAsync("aria-checked") != (active ? "true" : "false"))
        {
            await box.ClickAsync();
        }

        await Expect(box).ToHaveAttributeAsync("aria-checked", active ? "true" : "false");

        await ClickSaveAsync($"Save {kind}");
        return await SavedRevisionAsync();
    }

    /// <summary>Waits for the status beside the form to say the save went through, and returns the revision it names.</summary>
    protected async Task<int> SavedRevisionAsync()
    {
        var status = Page.GetByRole(AriaRole.Status).Filter(new() { HasText = "Master-data revision" });

        await Expect(status).ToContainTextAsync("Saved at Master-data revision");
        return int.Parse(Revision().Match(await status.InnerTextAsync()).Groups[1].Value, CultureInfo.InvariantCulture);
    }

    [GeneratedRegex(@"revision (\d+)")]
    private static partial Regex Revision();

    /// <summary>Submits a booking of 100 kg for the Shipper through the TMS's intake, as the intake Logic App does, and waits until the TMS has rated it. Returns the Transport Order number.</summary>
    protected async Task<string> SubmitRatedOrderAsync(string shipperId)
    {
        var booking = Bookings.Valid(TmsCalls.NewReference()).Weighing(100m);
        booking["shipperId"] = shipperId;
        var number = await Sync.SubmitAcceptedAsync(booking, TmsCalls.NewCorrelationId(), Ct);
        await Sync.WaitForRatingToSettleAsync(number, Ct);
        return number;
    }

}
