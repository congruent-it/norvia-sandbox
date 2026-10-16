using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace Norvia.Simulator.Web.UiTests.Reset;

/// <summary>
/// An operator returns the estate to a clean sheet from Configure (ADR 0039), and sees the cases where the reset is not offered or an application refuses it.
/// The edge cases of each application's door are in the integration tests.
/// </summary>
public sealed class ResetJourneyTests(SimulatorUiFixture simulator) : BrowserPage(simulator)
{
    private const int Orders = 3;

    // The reset waits until the Service Bus subscriptions are empty, which the workers take a moment to do after a run.
    private static readonly TimeSpan QuietPatience = TimeSpan.FromMinutes(3);

    private readonly SimulatorUiFixture _simulator = simulator;

    [Fact]
    public async Task AnOperatorResetsTheEstateAfterARun_ReadsWhatWasDeleted_AndTheNextRunStartsAtOne()
    {
        await RunToTheEndAsync();
        var reset = await OpenConfigureWhereResetIsOfferedAsync(_simulator.Address);

        await reset.ClickAsync();

        var confirmation = Page.GetByRole(AriaRole.Alertdialog, new() { Name = "Confirm reset" });
        await Expect(confirmation).ToContainTextAsync("This deletes, and can't be undone:");
        await Expect(confirmation).ToContainTextAsync("In the TMS: every Transport Order");
        await Expect(confirmation).ToContainTextAsync("In the Partner portal: every booking");
        await Expect(confirmation).ToContainTextAsync("In the Driver app: every Trip");
        await Expect(confirmation).ToContainTextAsync("In the Simulator: every run and log line");
        await Expect(confirmation).ToContainTextAsync("The Shippers, Carriers, Drivers, Vehicles, Rate Cards and Personas stay.");
        await confirmation.GetByRole(AriaRole.Button, new() { Name = "Delete everything and reset" }).ClickAsync();

        var result = Page.GetByRole(AriaRole.Status, new() { Name = "Reset result" });
        await Expect(result).ToContainTextAsync("The estate is a clean sheet.", new() { Timeout = 120_000 });
        await Expect(result).ToContainTextAsync("TMS: deleted");
        await Expect(result).ToContainTextAsync("Partner portal: deleted");
        await Expect(result).ToContainTextAsync("Driver app:");
        await Expect(result).ToContainTextAsync(new Regex(@"Simulator: deleted \d+ Runs, \d+ Log lines\."));
        await Expect(Page.GetByText("No runs yet.")).ToBeVisibleAsync();
        await Expect(Page.GetByText("The next run is number 1:")).ToBeVisibleAsync();
        await Expect(Page.GetByText("SIM-1-1", new() { Exact = true })).ToBeVisibleAsync();

        await StartRunAsync(bad: 0, assigned: 0);

        await Expect(Page.GetByRole(AriaRole.Heading, new() { Level = 1 })).ToHaveTextAsync("Run 1");
        await Expect(Page.GetByRole(AriaRole.Status).First).ToContainTextAsync($"Completed: {Orders} of {Orders} bookings sent", new() { Timeout = 120_000 });
        await Expect(Page.GetByRole(AriaRole.Table, new() { Name = "Log lines" }).GetByRole(AriaRole.Row).Filter(new() { HasText = "SIM-1-1" }).First).ToBeVisibleAsync();
    }

    [Fact]
    public async Task AnOperatorCancelsTheConfirmation_AndNothingIsDeleted()
    {
        await RunToTheEndAsync();
        var reset = await OpenConfigureWhereResetIsOfferedAsync(_simulator.Address);
        var runs = Page.GetByRole(AriaRole.Table, new() { Name = "Runs" });
        var before = await runs.GetByRole(AriaRole.Row).CountAsync();

        await reset.ClickAsync();
        await Page.GetByRole(AriaRole.Alertdialog, new() { Name = "Confirm reset" }).GetByRole(AriaRole.Button, new() { Name = "Keep everything" }).ClickAsync();

        await Expect(Page.GetByRole(AriaRole.Alertdialog)).ToHaveCountAsync(0);
        await Expect(Page.GetByRole(AriaRole.Status, new() { Name = "Reset result" })).ToHaveCountAsync(0);
        await Page.ReloadAsync();
        await Expect(runs.GetByRole(AriaRole.Row)).ToHaveCountAsync(before);
        await Expect(Page.GetByText("No runs yet.")).ToHaveCountAsync(0);
    }

    [Fact]
    public async Task AViewerWithoutTheRunRole_IsNotOfferedTheReset()
    {
        await Page.GotoAsync(_simulator.Viewer.ToString());

        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "You can't use the Simulator" })).ToBeVisibleAsync();
        await Expect(Page.GetByRole(AriaRole.Button, new() { Name = "Reset estate" })).ToHaveCountAsync(0);
    }

    [Fact]
    public async Task WhileARunIsActive_TheResetIsDisabled_AndSaysWhy()
    {
        await Page.GotoAsync("/");
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Level = 1 })).ToHaveTextAsync("Configure a run");
        await Page.GetByLabel("Preset").ClickAsync();
        await Page.GetByRole(AriaRole.Option, new() { Name = "Busy day" }).ClickAsync();
        await Page.GetByLabel("Orders").FillAsync(Orders.ToString(CultureInfo.InvariantCulture));
        await Page.GetByRole(AriaRole.Button, new() { Name = "Preview plan" }).ClickAsync();
        await Page.GetByRole(AriaRole.Button, new() { Name = "Start run" }).ClickAsync();
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Level = 1 })).ToContainTextAsync("Run ");
        var cancel = Page.GetByRole(AriaRole.Button, new() { Name = "Cancel run" });
        await Expect(cancel).ToBeVisibleAsync();

        try
        {
            await Page.GotoAsync("/");

            await Expect(Page.GetByRole(AriaRole.Button, new() { Name = "Reset estate", Exact = true })).ToBeDisabledAsync();
            await Expect(Page.GetByText("A run is active. Cancel it or wait for it to finish.")).ToBeVisibleAsync();
        }
        finally
        {
            // A paced run would keep sending for minutes into the next journey.
            await Page.GetByRole(AriaRole.Link, new() { Name = "Open it" }).ClickAsync();
            await cancel.ClickAsync();
            await Expect(cancel).ToHaveCountAsync(0);
        }
    }

    [Fact]
    public async Task WhereTheEstateDoesNotAllowTheReset_TheButtonIsDisabled_AndNamesTheApplication()
    {
        await Page.GotoAsync(_simulator.ResetSwitchedOff.ToString());

        await Expect(Page.GetByText("The reset is switched off for the TMS.")).ToBeVisibleAsync();
        await Expect(Page.GetByRole(AriaRole.Button, new() { Name = "Reset estate", Exact = true })).ToBeDisabledAsync();
    }

    [Fact]
    public async Task WhenAnApplicationRefuses_ThePageNamesItAndWhy_AndTheHistoryIsKept()
    {
        await RunToTheEndAsync();
        var reset = await OpenConfigureWhereResetIsOfferedAsync(_simulator.ResetRefused);
        var runs = Page.GetByRole(AriaRole.Table, new() { Name = "Runs" });
        var before = await runs.GetByRole(AriaRole.Row).CountAsync();

        await reset.ClickAsync();
        await Page.GetByRole(AriaRole.Alertdialog, new() { Name = "Confirm reset" }).GetByRole(AriaRole.Button, new() { Name = "Delete everything and reset" }).ClickAsync();

        var result = Page.GetByRole(AriaRole.Status, new() { Name = "Reset result" });
        await Expect(result).ToContainTextAsync("Not everything was reset.", new() { Timeout = 120_000 });
        await Expect(result).ToContainTextAsync($"TMS: refused. {ResetStub.RefusalDetail}");
        await Expect(result).Not.ToContainTextAsync("The estate is a clean sheet.");
        await Expect(runs.GetByRole(AriaRole.Row)).ToHaveCountAsync(before);
        await Expect(Page.GetByText("No runs yet.")).ToHaveCountAsync(0);
    }

    [Fact]
    public async Task AfterAReset_TheTmsHasNoTransportOrders_AndThePartnerPortalNoBookings_ButTheirReferenceDataStays()
    {
        // A Shipper books in the Partner portal, and the booking waits in the TMS for planning. (The Simulator books through the Partner API, which the portal does not list.)
        var reference = $"RST-{Guid.NewGuid():N}"[..16];
        var tms = await Page.Context.NewPageAsync();
        var portal = await Page.Context.NewPageAsync();
        var waiting = new Uri(_simulator.TmsUi, "/transport-orders").ToString();
        var bookings = new Uri(_simulator.PartnerPortal, "/personas/brouwer-dranken/bookings").ToString();
        await portal.GotoAsync(_simulator.PartnerPortal.ToString());
        await portal.GetByRole(AriaRole.Form, new() { Name = "Persona" }).GetByRole(AriaRole.Button, new() { Name = "Brouwer Dranken" }).ClickAsync();
        await Expect(portal.GetByRole(AriaRole.Main)).ToContainTextAsync("As Brouwer Dranken");
        await portal.GetByRole(AriaRole.Group, new() { Name = "Booking", Exact = true }).GetByLabel("Shipper reference", new() { Exact = true }).FillAsync(reference);
        await portal.GetByRole(AriaRole.Button, new() { Name = "Book transport" }).ClickAsync();
        await Expect(portal.GetByRole(AriaRole.Region, new() { Name = "Norvia's answer" })).ToContainTextAsync("Norvia accepted the booking as Transport Order", new() { Timeout = 30_000 });
        await portal.GotoAsync(bookings);
        await Expect(portal.GetByRole(AriaRole.Row).Filter(new() { HasText = reference })).ToBeVisibleAsync();
        await tms.GotoAsync(waiting);
        await Expect(tms.GetByRole(AriaRole.Row).Filter(new() { HasText = reference })).ToBeVisibleAsync();

        var reset = await OpenConfigureWhereResetIsOfferedAsync(_simulator.Address);
        await reset.ClickAsync();
        await Page.GetByRole(AriaRole.Alertdialog, new() { Name = "Confirm reset" }).GetByRole(AriaRole.Button, new() { Name = "Delete everything and reset" }).ClickAsync();
        await Expect(Page.GetByRole(AriaRole.Status, new() { Name = "Reset result" })).ToContainTextAsync("The estate is a clean sheet.", new() { Timeout = 120_000 });

        await tms.GotoAsync(waiting);
        await Expect(tms.GetByText("No Transport Orders are waiting for planning.")).ToBeVisibleAsync();
        await tms.GotoAsync(new Uri(_simulator.TmsUi, "/capacity").ToString());
        await Expect(tms.GetByRole(AriaRole.Table, new() { Name = "Vehicles" }).GetByRole(AriaRole.Row)).Not.ToHaveCountAsync(1);
        await portal.GotoAsync(bookings);
        await Expect(portal.GetByText("Brouwer Dranken hasn't sent Norvia a booking yet.")).ToBeVisibleAsync();
    }

    // Opens Configure and waits for the estate to be quiet: the reset is offered only once no subscription holds unprocessed messages, and the page checks that as it loads.
    private async Task<ILocator> OpenConfigureWhereResetIsOfferedAsync(Uri address)
    {
        var deadline = DateTime.UtcNow + QuietPatience;
        var button = Page.GetByRole(AriaRole.Button, new() { Name = "Reset estate", Exact = true });
        while (true)
        {
            await Page.GotoAsync(address.ToString());
            await Expect(Page.GetByRole(AriaRole.Heading, new() { Level = 1 })).ToHaveTextAsync("Configure a run");
            try
            {
                await Expect(button).ToBeEnabledAsync(new() { Timeout = 10_000 });
                return button;
            }
            catch (PlaywrightException) when (DateTime.UtcNow < deadline)
            {
                // Still busy: look again.
            }
        }
    }

    // A small run to the end, so the estate holds something to delete. Returns the Shipper references it sent.
    private async Task<string[]> RunToTheEndAsync(int bad = 25, int assigned = 80)
    {
        await Page.GotoAsync("/");
        await StartRunAsync(bad, assigned);
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Level = 1 })).ToContainTextAsync("Run ");
        await Expect(Page.GetByRole(AriaRole.Status).First).ToContainTextAsync($"Completed: {Orders} of {Orders} bookings sent", new() { Timeout = 120_000 });
        var rows = Page.GetByRole(AriaRole.Table, new() { Name = "Log lines" }).GetByRole(AriaRole.Row).Filter(new() { HasText = "Send booking" });
        await Expect(rows).ToHaveCountAsync(Orders);
        return [.. (await rows.AllInnerTextsAsync()).Select(row => Regex.Match(row, @"SIM-\d+-\d+").Value)];
    }

    private async Task StartRunAsync(int bad, int assigned)
    {
        await Page.GotoAsync("/");
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Level = 1 })).ToHaveTextAsync("Configure a run");
        await Page.GetByLabel("Orders").FillAsync(Orders.ToString(CultureInfo.InvariantCulture));
        await Page.GetByLabel("Bad bookings (%)").FillAsync(bad.ToString(CultureInfo.InvariantCulture));
        await Page.GetByLabel("Assigned by the Planner (%)").FillAsync(assigned.ToString(CultureInfo.InvariantCulture));
        await Page.GetByLabel("Seed").FillAsync(Random.Shared.Next(1_000, int.MaxValue).ToString(CultureInfo.InvariantCulture));
        await Page.GetByRole(AriaRole.Button, new() { Name = "Preview plan" }).ClickAsync();
        await Page.GetByRole(AriaRole.Button, new() { Name = "Start run" }).ClickAsync();
    }
}
