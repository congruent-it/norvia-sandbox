using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace Norvia.Tms.Web.UiTests.Shipments;

/// <summary>
/// The Planner releases a Shipment to a Carrier, finds its Carrier booking in the bookings awaiting an answer, withdraws
/// it, and sees the Shipment back in Shipments to release with the outcome of that booking.
/// </summary>
public sealed class WithdrawBookingJourneyTests(TmsUiFixture tms) : BrowserPage(tms)
{
    [Fact]
    public async Task AShipmentReleasedToACarrier_Withdrawn_LeavesTheBookingsAwaitingAnAnswer_AndIsBackToReleaseWithTheOutcome()
    {
        var shipperReference = $"UI-{Guid.NewGuid():N}"[..20];
        var number = await SubmitAcceptedAsync(Bookings.Valid(shipperReference));

        await Page.GotoAsync("/");
        await Page.GetByRole(AriaRole.Button, new() { Name = "Planner" }).ClickAsync();
        await OpenViewAsync("Waiting for planning");
        var order = Page.GetByRole(AriaRole.Row).Filter(new() { HasText = shipperReference });
        await order.GetByRole(AriaRole.Checkbox, new() { Name = $"Select {number}" }).ClickAsync();
        var assign = Page.GetByRole(AriaRole.Group, new() { Name = "Assign" });
        await assign.GetByRole(AriaRole.Combobox, new() { Name = "Assign to" }).ClickAsync();
        await Page.GetByRole(AriaRole.Option, new() { Name = "Van Dijk Transport" }).ClickAsync();
        await assign.GetByRole(AriaRole.Button, new() { Name = "Assign selected" }).ClickAsync();
        var planned = Page.GetByRole(AriaRole.List, new() { Name = "Assignment outcomes" }).GetByRole(AriaRole.Status);
        await Expect(planned).ToContainTextAsync($"Transport Order {number} is planned as Shipment");
        var shipmentNumber = await planned.GetByRole(AriaRole.Link).TextContentAsync();

        await OpenViewAsync("Shipments to release");
        var toRelease = Page.GetByRole(AriaRole.Row).Filter(new() { HasText = shipmentNumber });
        await toRelease.GetByRole(AriaRole.Checkbox, new() { Name = $"Select {shipmentNumber}" }).ClickAsync();
        await Page.GetByRole(AriaRole.Group, new() { Name = "Release" }).GetByRole(AriaRole.Button, new() { Name = "Release selected" }).ClickAsync();
        await Expect(Page.GetByRole(AriaRole.List, new() { Name = "Release outcomes" }).GetByRole(AriaRole.Status))
            .ToContainTextAsync($"Shipment {shipmentNumber} is released.");
        await Expect(toRelease).ToHaveCountAsync(0);

        await OpenViewAsync("Bookings awaiting an answer");
        await Expect(Heading("Bookings awaiting an answer")).ToBeVisibleAsync();
        var awaiting = Page.GetByRole(AriaRole.Row).Filter(new() { HasText = shipmentNumber });
        await Expect(awaiting).ToContainTextAsync("Van Dijk Transport");

        await awaiting.GetByRole(AriaRole.Button, new() { Name = "Withdraw CB-", Exact = false }).ClickAsync();
        await Expect(Page.GetByRole(AriaRole.List, new() { Name = "Withdrawal outcomes" }).GetByRole(AriaRole.Status))
            .ToContainTextAsync("is withdrawn");
        await Expect(awaiting).ToHaveCountAsync(0);

        await OpenViewAsync("Shipments to release");
        var backToRelease = Page.GetByRole(AriaRole.Row).Filter(new() { HasText = shipmentNumber });
        await Expect(backToRelease).ToContainTextAsync("Withdrawn from Van Dijk Transport");
    }

    private ILocator Heading(string name) => Page.GetByRole(AriaRole.Heading, new() { Name = name, Level = 1 });
}
