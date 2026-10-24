using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace Norvia.Tms.Web.UiTests.Faults;

/// <summary>The Partner portal's presenter panel, as the presenter uses it to switch a fault on for a while and off again.</summary>
public sealed class PresenterPanel(IPage portal, Uri partnerPortal)
{
    public const string TmsUnavailable = "tms-unavailable";
    public const string DriverAppHeicPhoto = "driver-app-heic-photo";

    /// <summary>Starts from a fault that is off, wherever an earlier failed run left it.</summary>
    public async Task SwitchOnAsync(string fault, string duration)
    {
        var group = await OpenAsync(fault);
        var off = group.GetByRole(AriaRole.Button, new() { Name = $"Switch off {fault}" });
        if (await off.IsEnabledAsync())
        {
            await off.ClickAsync();
            await Expect(off).ToBeDisabledAsync();
        }

        // The duration is a Fluent UI select, a custom element: open it and pick the option.
        var select = group.GetByRole(AriaRole.Combobox, new() { Name = "Duration" });
        await select.ClickAsync();
        await portal.GetByRole(AriaRole.Option, new() { Name = duration, Exact = true }).ClickAsync();
        await Expect(select).ToHaveAttributeAsync("aria-expanded", "false");
        await group.GetByRole(AriaRole.Button, new() { Name = $"Switch on {fault}" }).ClickAsync();
        await Expect(group.GetByRole(AriaRole.Status)).ToContainTextAsync("On");
    }

    public async Task SwitchOffAsync(string fault)
    {
        var group = await OpenAsync(fault);
        var off = group.GetByRole(AriaRole.Button, new() { Name = $"Switch off {fault}" });
        if (await off.IsEnabledAsync())
        {
            await off.ClickAsync();
        }

        await Expect(off).ToBeDisabledAsync();
    }

    private async Task<ILocator> OpenAsync(string fault)
    {
        await portal.GotoAsync(new Uri(partnerPortal, "/presenter").ToString());
        await Expect(portal.GetByRole(AriaRole.Heading, new() { Name = "Presenter panel", Level = 1 })).ToBeVisibleAsync();
        var group = portal.GetByRole(AriaRole.Group, new() { Name = fault, Exact = true });
        await Expect(group).ToBeVisibleAsync(new() { Timeout = 30_000 });
        return group;
    }
}
