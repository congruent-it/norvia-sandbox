using System.Text.RegularExpressions;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace Norvia.Tms.Web.UiTests.Shipments;

/// <summary>
/// Kessler Spedition and PakketPunt Express answer their booking requests on their own, through the same path as a
/// presenter's click: the presenter releases a Shipment and the booking is answered without touching the Partner portal.
/// </summary>
public sealed class AutoAnswerJourneyTests(TmsUiFixture tms) : BrowserPage(tms)
{
    private readonly TmsUiFixture _tms = tms;

    // The seed's Carrier Personas answer by hand, because the Simulator plays them, so each test switches its Persona to the mode it needs and back.
    [Fact]
    public async Task KesslerSpeditionConfirmsOnItsOwn_WhenThePresenterSwitchesItToAutoConfirm_TheShipmentIsBooked_WithAReferenceInItsPattern()
    {
        var modePanel = await OpenAnswerModeAsync("kessler-spedition");
        await SaveModeAsync(modePanel, "AutoConfirm");
        try
        {
            var (shipmentNumber, bookingNumber) = await ReleaseToCarrierAsync("Kessler Spedition");

            // The portal's inbox refreshes itself, so it says when the Persona has answered; the TMS's pages are read after that.
            await WaitForAnswerAsync("kessler-spedition", bookingNumber, "Confirmed");
            await Page.GotoAsync($"/shipments/{shipmentNumber}");
            await Expect(Page.GetByRole(AriaRole.Table, new() { Name = "Carrier bookings" }).GetByRole(AriaRole.Row).Filter(new() { HasText = bookingNumber }))
                .ToContainTextAsync(new Regex(@"Carrier booking reference KES-\d{6}-\d{4}"));
            await Expect(Page.GetByText("Booked", new() { Exact = true }).First).ToBeVisibleAsync();
        }
        finally
        {
            await SaveModeAsync(modePanel, "Manual");
        }
    }

    [Fact]
    public async Task PakketPuntExpressRejectsOnItsOwn_WhenThePresenterSwitchesItToAutoReject_AndNotBefore()
    {
        var modePanel = await OpenAnswerModeAsync("pakketpunt-express");
        await Expect(modePanel.GetByLabel("Mode")).ToHaveValueAsync("Manual");

        await SaveModeAsync(modePanel, "AutoReject", rejectionReason: "GoodsNotAccepted");
        try
        {
            var (shipmentNumber, bookingNumber) = await ReleaseToCarrierAsync("PakketPunt Express");

            await WaitForAnswerAsync("pakketpunt-express", bookingNumber, "Rejected");
            await Page.GotoAsync("/shipments/to-release");
            await Expect(Page.GetByRole(AriaRole.Row).Filter(new() { HasText = shipmentNumber })).ToContainTextAsync("Rejected by PakketPunt Express");
        }
        finally
        {
            await SaveModeAsync(modePanel, "Manual");
        }
    }

    private async Task<ILocator> OpenAnswerModeAsync(string persona)
    {
        var portal = await Page.Context.NewPageAsync();
        await portal.GotoAsync(new Uri(_tms.PartnerPortal, $"/personas/{persona}/booking-requests").ToString());
        return portal.GetByRole(AriaRole.Group, new() { Name = "Answer mode" });
    }

    private static async Task SaveModeAsync(ILocator modePanel, string mode, string? rejectionReason = null)
    {
        await modePanel.GetByLabel("Mode").SelectOptionAsync(mode);
        if (rejectionReason is not null)
        {
            await modePanel.GetByLabel("Rejection reason").SelectOptionAsync(rejectionReason);
        }

        if (mode != "Manual")
        {
            await modePanel.GetByLabel("Delay before the answer (seconds)").FillAsync("1");
        }

        await modePanel.GetByRole(AriaRole.Button, new() { Name = "Save answer mode" }).ClickAsync();
        await Expect(modePanel.GetByRole(AriaRole.Status)).ToContainTextAsync("Saved");
    }

    private async Task WaitForAnswerAsync(string persona, string bookingNumber, string answer)
    {
        var inbox = await Page.Context.NewPageAsync();
        await inbox.GotoAsync(new Uri(_tms.PartnerPortal, $"/personas/{persona}/booking-requests").ToString());
        await Expect(inbox.GetByRole(AriaRole.Row).Filter(new() { HasText = bookingNumber })).ToContainTextAsync(answer, new() { Timeout = 30_000 });
    }
}
