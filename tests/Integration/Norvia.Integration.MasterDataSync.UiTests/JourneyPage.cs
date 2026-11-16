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
    private readonly List<string> _console = [];
    private bool _listening;

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
        if (!_listening)
        {
            _listening = true;
            Page.Console += (_, message) => _console.Add($"{message.Type}: {message.Text}");
            Page.PageError += (_, error) => _console.Add($"pageerror: {error}");
            Page.RequestFailed += (_, request) => _console.Add($"requestfailed: {request.Url} {request.Failure}");
        }

        await Page.GotoAsync(path);
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
    }

    /// <summary>
    /// Presses a form's Save button until the form answers, with what it saved or why it refused. A page whose circuit hasn't started ignores a press, and a form answers at once when it has.
    /// A press that gets an answer isn't repeated.
    /// </summary>
    protected async Task ClickSaveAsync(string button)
    {
        var answer = Page.GetByRole(AriaRole.Status).Filter(new() { HasText = "Master-data revision" }).Or(Page.GetByRole(AriaRole.Alert));
        for (var attempt = 1; ; attempt++)
        {
            await Page.GetByRole(AriaRole.Button, new() { Name = button }).ClickAsync();
            try
            {
                await answer.First.WaitForAsync(new() { Timeout = 3000 });
                return;
            }
            catch (TimeoutException) when (attempt < 10)
            {
                // Not started yet: press again.
            }
            catch (TimeoutException exception)
            {
                throw new TimeoutException($"The form never answered. The browser said:{Environment.NewLine}{string.Join(Environment.NewLine, _console.TakeLast(15))}", exception);
            }
        }
    }

    /// <summary>Creates a Shipper in the ERP's form, and returns the Master-data revision the ERP says it saved.</summary>
    protected async Task<int> CreateShipperAsync(string id, string name)
    {
        await OpenErpAsync("/shippers/new");
        await Page.GetByRole(AriaRole.Textbox, new() { Name = "ID", Exact = true }).FillAsync(id);
        await Page.GetByRole(AriaRole.Textbox, new() { Name = "Name", Exact = true }).FillAsync(name);
        await ClickSaveAsync("Save Shipper");
        return await SavedRevisionAsync(created: true);
    }

    /// <summary>Creates a Rate Card of a Shipper in the ERP's form with the versions as JSON in the shape of the ERP API's body, and returns the revision the ERP says it saved.</summary>
    protected async Task<int> CreateRateCardAsync(string id, string shipperId, JsonArray versions)
    {
        await OpenErpAsync("/rate-cards/new");
        await Page.GetByRole(AriaRole.Textbox, new() { Name = "ID", Exact = true }).FillAsync(id);
        await Page.GetByRole(AriaRole.Combobox, new() { Name = "Shipper" }).ClickAsync();
        await Page.GetByRole(AriaRole.Option, new() { Name = shipperId, Exact = false }).ClickAsync();
        await Page.GetByRole(AriaRole.Textbox, new() { Name = "Versions (JSON)", Exact = true }).FillAsync(versions.ToJsonString());
        await ClickSaveAsync("Save Rate Card");
        return await SavedRevisionAsync(created: true);
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
    protected async Task<int> SavedRevisionAsync(bool created = false)
    {
        var status = Page.GetByRole(AriaRole.Status).Filter(new() { HasText = "Master-data revision" });

        // A new record's form moves on to the record's edit page as soon as it is saved, so a press that was sent again lands there and finds nothing to change.
        await Expect(status).ToContainTextAsync(new Regex(created ? @"Saved at Master-data revision|Nothing changed" : "Saved at Master-data revision"));
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

    /// <summary>The Transport Order's page in the TMS UI.</summary>
    protected Task<IPage> OpenTransportOrderAsync(string number) => OpenTmsAsync($"/transport-orders/{number}");
}
