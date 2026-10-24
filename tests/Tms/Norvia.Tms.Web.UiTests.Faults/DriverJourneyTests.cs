using System.Diagnostics;
using System.Text.RegularExpressions;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace Norvia.Tms.Web.UiTests.Faults;

/// <summary>
/// The as-found faults of the TMS and the Driver app (P12), switched on from the presenter panel and seen on the Driver's phone and in the TMS UI beside it. Each journey gets a Trip of its own
/// and switches its fault off again, even when it fails. The messages and the Message trail are covered by the integration tests.
/// </summary>
public sealed partial class DriverJourneyTests(FaultsUiFixture norvia) : BrowserPages(norvia)
{
    // The Trip dispatch worker sends the Trip to the Driver app, and the Driver reports worker takes a report to the TMS, a little after the Driver or the Planner acts.
    private const float PatienceMilliseconds = 90_000;

    // A valid 1x1 PNG.
    private const string Png = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg==";

    [Fact]
    public async Task WithTheTmsUnavailable_TheDriversReportWaits_AndTheTripProgresses_OnceTheTmsIsBack()
    {
        Assert.SkipWhen(TripCalls.TodayIsAboutToEnd(), "A Trip is planned for today, and today is about to end.");
        var trip = await Norvia.DispatchTripForTodayAsync(FaultsUiFixture.FirstDriver, "VEH-0002", TestContext.Current.CancellationToken);
        await SignInAsync(trip.DriverId);
        var card = Driver.GetByRole(AriaRole.Region, new() { Name = $"Trip {trip.TripNumber}" });
        await ReloadUntilVisibleAsync(Driver, card);
        await Panel.SwitchOnAsync(PresenterPanel.TmsUnavailable, "2 minutes");
        try
        {
            await Driver.GotoAsync(new Uri(Norvia.DriverApp, "/").ToString());
            await card.GetByRole(AriaRole.Button, new() { Name = "Start Trip", Exact = true }).ClickAsync();

            // The Driver app takes the report in and keeps it in the Driver's session while the TMS refuses.
            await Expect(Driver.GetByRole(AriaRole.Listitem, new() { Name = "Start Trip: Sending" })).ToBeVisibleAsync();
        }
        finally
        {
            await Panel.SwitchOffAsync(PresenterPanel.TmsUnavailable);
        }

        // The Driver app asks again by itself while a report is Sending; once the TMS is back the report goes through, and the Planner sees the Trip In progress.
        await Driver.GotoAsync(new Uri(Norvia.DriverApp, "/").ToString());
        await Expect(Driver.GetByRole(AriaRole.Listitem, new() { Name = "Start Trip: Recorded" })).ToBeVisibleAsync(new() { Timeout = PatienceMilliseconds });
        await Tms.GotoAsync(new Uri(Norvia.TmsWeb, $"/trips/{trip.TripNumber}").ToString());
        await Expect(Tms.GetByLabel("Trip", new() { Exact = true })).ToContainTextAsync("InProgress", new() { Timeout = PatienceMilliseconds });
    }

    [Fact]
    public async Task APhotoTheDriverAppSendsAsHeic_IsRefusedByTheTms_AndTheDriverSeesTheProofOfDeliveryRefused()
    {
        Assert.SkipWhen(TripCalls.TodayIsAboutToEnd(), "A Trip is planned for today, and today is about to end.");
        var trip = await Norvia.DispatchTripForTodayAsync(FaultsUiFixture.SecondDriver, "VEH-0001", TestContext.Current.CancellationToken);
        await SignInAsync(trip.DriverId);
        var card = Driver.GetByRole(AriaRole.Region, new() { Name = $"Trip {trip.TripNumber}" });
        await ReloadUntilVisibleAsync(Driver, card);
        await card.GetByRole(AriaRole.Button, new() { Name = "Start Trip", Exact = true }).ClickAsync();
        await Expect(Driver.GetByRole(AriaRole.Listitem, new() { Name = "Start Trip: Recorded" })).ToBeVisibleAsync(new() { Timeout = PatienceMilliseconds });

        await Driver.GotoAsync(StopScreen(trip, "collection"));
        await Driver.GetByRole(AriaRole.Button, new() { Name = "Collected", Exact = true }).ClickAsync();
        await Driver.GotoAsync(StopScreen(trip, "collection"));
        await ReloadUntilVisibleAsync(Driver, Driver.GetByRole(AriaRole.Listitem, new() { Name = $"Collected {trip.ShipmentNumber}: Recorded" }));

        await Panel.SwitchOnAsync(PresenterPanel.DriverAppHeicPhoto, "2 minutes");
        try
        {
            // The Driver delivers as usual: the Consignee signs and the Driver adds a photo. The Driver app's API sends the photo as HEIC while the fault is on; the Driver's screens run in a process of
            // their own, which the fault doesn't reach, so the delivery goes through the API the Driver app's clients call, and the Driver reads the answer on the screen.
            await Norvia.DeliverAsync(trip, Png);

            // The Delivered report is recorded; the TMS refuses the Proof of delivery's file, and the Driver reads why.
            await Driver.GotoAsync(StopScreen(trip, "delivery"));
            var refused = Driver.GetByRole(AriaRole.Listitem, new() { NameRegex = ProofOfDeliveryRefused() });
            await ReloadUntilVisibleAsync(Driver, refused);
            await Expect(refused).ToContainTextAsync("FileNotAccepted");
            await Expect(Driver.GetByRole(AriaRole.Listitem, new() { Name = $"Delivered {trip.ShipmentNumber}: Recorded" })).ToBeVisibleAsync();
        }
        finally
        {
            await Panel.SwitchOffAsync(PresenterPanel.DriverAppHeicPhoto);
        }
    }

    // Signs in with the stand-in for Entra ID that lists the seeded Drivers.
    private async Task SignInAsync(string driverId)
    {
        await Driver.GotoAsync(new Uri(Norvia.DriverApp, "/local/sign-in").ToString());
        await Driver.GetByRole(AriaRole.Button, new() { Name = driverId }).ClickAsync();
        await Expect(Driver.GetByRole(AriaRole.Heading, new() { Level = 1 })).ToBeVisibleAsync();
    }

    private string StopScreen(DispatchedTrip trip, string kind) => new Uri(Norvia.DriverApp, $"/trips/{trip.TripNumber}/stops/{trip.ShipmentNumber}/{kind}").ToString();

    // The Driver app shows a report's answer as the page is loaded, so the page is loaded again until the answer is there.
    private static async Task ReloadUntilVisibleAsync(IPage page, ILocator expected)
    {
        var timer = Stopwatch.StartNew();
        while (true)
        {
            try
            {
                await Expect(expected).ToBeVisibleAsync(new() { Timeout = 3_000 });
                return;
            }
            catch (PlaywrightException) when (timer.ElapsedMilliseconds < PatienceMilliseconds)
            {
                await page.ReloadAsync();
            }
        }
    }

    [GeneratedRegex(@"^Proof of delivery.*: Refused$")]
    private static partial Regex ProofOfDeliveryRefused();
}
