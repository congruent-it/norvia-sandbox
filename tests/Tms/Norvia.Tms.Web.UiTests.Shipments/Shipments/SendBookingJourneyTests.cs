using System.Text.RegularExpressions;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace Norvia.Tms.Web.UiTests.Shipments;

/// <summary>
/// The Planner releases a Shipment to Van Dijk Transport. Locally the booking worker plays the P4 booking Logic App (ADR
/// 0030): the request reaches Van Dijk's inbox in the Partner portal, and the booking shows as sent, with its sent time,
/// in the bookings awaiting an answer.
/// </summary>
public sealed class SendBookingJourneyTests(TmsUiFixture tms) : BrowserPage(tms)
{
    private readonly TmsUiFixture _tms = tms;

    [Fact]
    public async Task AShipmentReleasedToACarrier_ReachesItsInbox_AndShowsAsSentToThePlanner()
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

        // The worker sends the request and reports it sent; the list says "Not yet sent" until it has.
        await OpenViewAsync("Bookings awaiting an answer");
        var awaiting = Page.GetByRole(AriaRole.Row).Filter(new() { HasText = shipmentNumber });
        await Expect(awaiting).ToContainTextAsync("Van Dijk Transport");
        await Expect(awaiting).Not.ToContainTextAsync("Not yet sent");
        // The booking number is on the row's Withdraw button, which names it.
        var withdraw = awaiting.GetByRole(AriaRole.Button, new() { Name = "Withdraw CB-", Exact = false });
        var bookingNumber = Regex.Match(await withdraw.GetAttributeAsync("aria-label") ?? await withdraw.InnerTextAsync(), @"CB-\d{7}").Value;
        bookingNumber.Should().NotBeEmpty();

        await Page.GotoAsync(new Uri(_tms.PartnerPortal, "/personas/van-dijk-transport/booking-requests").ToString());
        var request = Page.GetByRole(AriaRole.Row).Filter(new() { HasText = bookingNumber });
        await Expect(request).ToContainTextAsync(shipmentNumber!);
        await Expect(request).ToContainTextAsync("NLD");
        await Expect(request).Not.ToContainTextAsync(shipperReference);
    }
}
