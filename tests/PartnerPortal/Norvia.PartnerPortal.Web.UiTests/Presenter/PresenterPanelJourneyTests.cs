using System.Text.RegularExpressions;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace Norvia.PartnerPortal.Web.UiTests.Presenter;

/// <summary>
/// The presenter plays with the faults on the panel (P11): every fault is listed with its owner, state and remaining time, switched on for a duration
/// and off again, and it ends by itself at expiry. The only fault is the Driver app's, and these journeys are the only ones that touch it; each leaves it
/// off, and starts by switching it off, so none depends on another.
/// </summary>
public sealed class PresenterPanelJourneyTests(PartnerPortalUiFixture norvia) : BrowserPages(norvia)
{
    private readonly PartnerPortalUiFixture _norvia = norvia;

    private const string Fault = "driver-app-unavailable";

    // A fault outlives a test only if the test failed; the expiry test waits the whole minute the shortest duration lasts.
    private static readonly float Expiry = 90_000;

    private ILocator FaultGroup => Portal.GetByRole(AriaRole.Group, new() { Name = Fault, Exact = true });

    private ILocator SwitchOn => FaultGroup.GetByRole(AriaRole.Button, new() { Name = $"Switch on {Fault}" });

    private ILocator SwitchOff => FaultGroup.GetByRole(AriaRole.Button, new() { Name = $"Switch off {Fault}" });

    [Fact]
    public async Task ThePanel_ListsTheFault_WithItsOwner_AndItsStateOff()
    {
        await OpenPanelAsync();

        await Expect(FaultGroup.GetByRole(AriaRole.Heading, new() { Name = Fault })).ToBeVisibleAsync();
        await Expect(FaultGroup).ToContainTextAsync("Owner: driver-app");
        await Expect(FaultGroup.GetByRole(AriaRole.Status)).ToHaveTextAsync("Off");
        await Expect(SwitchOff).ToBeDisabledAsync();
    }

    [Fact]
    public async Task SwitchingAFaultOn_WithADuration_ShowsItOnWithTheTimeLeftCountingDown_AndOffPutsItBack()
    {
        await OpenPanelAsync();

        await ChooseOneMinuteAsync();
        await SwitchOn.ClickAsync();

        var status = FaultGroup.GetByRole(AriaRole.Status);
        await Expect(status).ToHaveTextAsync(new Regex(@"On\s*,\s*[01]:\d\d left"));
        // It is counting down: a minute from the start can't still read 0:5x once the page has ticked a few seconds on.
        await Expect(status).ToHaveTextAsync(new Regex(@"0:[0-4]\d left"), new() { Timeout = 30_000 });

        await SwitchOff.ClickAsync();

        await Expect(status).ToHaveTextAsync("Off");
        await Expect(SwitchOff).ToBeDisabledAsync();
    }

    [Fact]
    public async Task AFaultSwitchedOn_EndsByItselfAtExpiry_AndThePanelShowsItOff()
    {
        await OpenPanelAsync();
        await ChooseOneMinuteAsync();
        await SwitchOn.ClickAsync();
        var status = FaultGroup.GetByRole(AriaRole.Status);
        await Expect(status).ToContainTextAsync("On");

        await Expect(status).ToHaveTextAsync("Off", new() { Timeout = Expiry });
        await Expect(SwitchOff).ToBeDisabledAsync();
    }

    // The duration is a Fluent UI select, a custom element: open it and pick the option.
    private async Task ChooseOneMinuteAsync()
    {
        await FaultGroup.GetByRole(AriaRole.Combobox, new() { Name = "Duration" }).ClickAsync();
        await Portal.GetByRole(AriaRole.Option, new() { Name = "1 minute", Exact = true }).ClickAsync();
        await Expect(FaultGroup.GetByRole(AriaRole.Combobox, new() { Name = "Duration" })).ToHaveAttributeAsync("aria-expanded", "false");
    }

    // Starts every journey from a fault that is off, wherever an earlier failed run left it.
    private async Task OpenPanelAsync()
    {
        await Portal.GotoAsync(new Uri(_norvia.PartnerPortal, "/presenter").ToString());
        await Expect(Portal.GetByRole(AriaRole.Heading, new() { Name = "Presenter panel", Level = 1 })).ToBeVisibleAsync();
        await Expect(FaultGroup).ToBeVisibleAsync(new() { Timeout = 30_000 });
        if (await SwitchOff.IsEnabledAsync())
        {
            await SwitchOff.ClickAsync();
        }

        await Expect(SwitchOff).ToBeDisabledAsync();
    }
}
