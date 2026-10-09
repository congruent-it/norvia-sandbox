using System.Text.RegularExpressions;
using Microsoft.Playwright;
using Norvia.Tms.Api.IntegrationTests.TransportOrders;
using static Microsoft.Playwright.Assertions;

namespace Norvia.Tms.Web.UiTests.Shipments;

/// <summary>
/// Van Dijk Transport answers the booking request in its inbox in the Partner portal. The answer goes through the local
/// stand-in for API Management and the Carrier-answer Logic App to the TMS, and comes back to the portal: the Planner sees
/// the booking Confirmed and the Shipment Booked, or the Shipment back in Shipments to release with the reason.
/// </summary>
public sealed class AnswerBookingJourneyTests(TmsUiFixture tms) : BrowserPage(tms)
{
    private readonly TmsUiFixture _tms = tms;

    [Fact]
    public async Task VanDijkConfirmsTheRequest_TheBookingIsConfirmedAndTheShipmentBooked_WithTheCarriersOwnReference()
    {
        var (shipmentNumber, bookingNumber) = await ReleaseToVanDijkAsync();
        var reference = $"VDT-{Guid.NewGuid():N}"[..12];

        var inbox = await OpenInboxAsync();
        var request = inbox.GetByRole(AriaRole.Row).Filter(new() { HasText = bookingNumber });
        await request.GetByRole(AriaRole.Button, new() { Name = $"Confirm {bookingNumber}" }).ClickAsync();
        var form = inbox.GetByRole(AriaRole.Region, new() { Name = $"Confirm {bookingNumber}" });
        await form.GetByRole(AriaRole.Button, new() { Name = "Confirm booking" }).ClickAsync();
        await Expect(form).ToContainTextAsync("Enter your own booking reference.");
        await form.GetByLabel("Carrier booking reference").FillAsync(reference);
        await form.GetByLabel("Planned collection time (optional, Amsterdam time)").FillAsync("2030-10-02T08:30");
        await form.GetByRole(AriaRole.Button, new() { Name = "Confirm booking" }).ClickAsync();

        await Expect(inbox.GetByRole(AriaRole.Status)).ToContainTextAsync($"Norvia recorded your confirmation of {bookingNumber}");
        await Expect(request).ToContainTextAsync($"Confirmed, your reference {reference}");

        await Page.GotoAsync($"/shipments/{shipmentNumber}");
        var booking = Page.GetByRole(AriaRole.Table, new() { Name = "Carrier bookings" }).GetByRole(AriaRole.Row).Filter(new() { HasText = bookingNumber });
        await Expect(booking).ToContainTextAsync($"Carrier booking reference {reference}");
        await Expect(Page.GetByText("Booked", new() { Exact = true }).First).ToBeVisibleAsync();
        await Page.GetByRole(AriaRole.Link, new() { Name = "Bookings awaiting an answer" }).ClickAsync();
        await Expect(Page.GetByRole(AriaRole.Row).Filter(new() { HasText = shipmentNumber })).ToHaveCountAsync(0);
    }

    [Fact]
    public async Task VanDijkRejectsTheRequestForOther_TheShipmentIsBackInShipmentsToRelease_WithTheReason()
    {
        var (shipmentNumber, bookingNumber) = await ReleaseToVanDijkAsync();

        var inbox = await OpenInboxAsync();
        var request = inbox.GetByRole(AriaRole.Row).Filter(new() { HasText = bookingNumber });
        await request.GetByRole(AriaRole.Button, new() { Name = $"Reject {bookingNumber}" }).ClickAsync();
        var form = inbox.GetByRole(AriaRole.Region, new() { Name = $"Reject {bookingNumber}" });
        await form.GetByRole(AriaRole.Combobox, new() { Name = "Reason" }).ClickAsync();
        await Portal.GetByRole(AriaRole.Option, new() { Name = "Other" }).ClickAsync();
        await form.GetByRole(AriaRole.Button, new() { Name = "Reject booking" }).ClickAsync();
        await Expect(form).ToContainTextAsync("Say why, when the reason is Other.");
        await form.GetByRole(AriaRole.Textbox, new() { Name = "Reason text" }).FillAsync("No trucks on Friday");
        await form.GetByRole(AriaRole.Button, new() { Name = "Reject booking" }).ClickAsync();

        await Expect(inbox.GetByRole(AriaRole.Status)).ToContainTextAsync($"Norvia recorded your rejection of {bookingNumber}");
        await Expect(request).ToContainTextAsync("Rejected: Other, No trucks on Friday");

        await Page.GotoAsync("/shipments/to-release");
        await Expect(Page.GetByRole(AriaRole.Row).Filter(new() { HasText = shipmentNumber })).ToContainTextAsync("Rejected by Van Dijk Transport: No trucks on Friday");
    }

    [Fact]
    public async Task VanDijkAnswersARequestThePlannerWithdrew_TheInboxShowsItWithdrawnByNorvia()
    {
        var (shipmentNumber, bookingNumber) = await ReleaseToVanDijkAsync();
        var inbox = await OpenInboxAsync();
        var request = inbox.GetByRole(AriaRole.Row).Filter(new() { HasText = bookingNumber });
        await request.GetByRole(AriaRole.Button, new() { Name = $"Confirm {bookingNumber}" }).ClickAsync();
        var form = inbox.GetByRole(AriaRole.Region, new() { Name = $"Confirm {bookingNumber}" });
        await form.GetByLabel("Carrier booking reference").FillAsync("VDT-LATE-1");

        // The Planner withdraws while Van Dijk is still answering.
        await Page.GotoAsync("/shipments/awaiting-answer");
        await Page.GetByRole(AriaRole.Row).Filter(new() { HasText = shipmentNumber }).GetByRole(AriaRole.Button, new() { Name = $"Withdraw {bookingNumber}" }).ClickAsync();
        await Expect(Page.GetByRole(AriaRole.List, new() { Name = "Withdrawal outcomes" }).GetByRole(AriaRole.Status)).ToContainTextAsync("is withdrawn");

        await form.GetByRole(AriaRole.Button, new() { Name = "Confirm booking" }).ClickAsync();

        await Expect(inbox.GetByRole(AriaRole.Alert)).ToContainTextAsync($"Norvia no longer has {bookingNumber} open");
        await Expect(request).ToContainTextAsync("Withdrawn by Norvia");
    }

    private IPage Portal { get; set; } = null!;

    private ILocator InboxPage => Portal.Locator("main");

    private async Task<ILocator> OpenInboxAsync()
    {
        Portal ??= await Page.Context.NewPageAsync();
        await Portal.GotoAsync(new Uri(_tms.PartnerPortal, "/personas/van-dijk-transport/booking-requests").ToString());
        return InboxPage;
    }

    /// <summary>Plans a new Transport Order to Van Dijk Transport, releases it, and waits until its request is sent to the inbox.</summary>
    private async Task<(string ShipmentNumber, string BookingNumber)> ReleaseToVanDijkAsync()
    {
        var shipperReference = $"UI-{Guid.NewGuid():N}"[..20];
        var number = await SubmitAcceptedAsync(Bookings.Valid(shipperReference));

        await Page.GotoAsync("/");
        await Page.GetByRole(AriaRole.Button, new() { Name = "Planner" }).ClickAsync();
        var order = Page.GetByRole(AriaRole.Row).Filter(new() { HasText = shipperReference });
        await order.GetByRole(AriaRole.Checkbox, new() { Name = $"Select {number}" }).ClickAsync();
        var assign = Page.GetByRole(AriaRole.Group, new() { Name = "Assign" });
        await assign.GetByRole(AriaRole.Combobox, new() { Name = "Assign to" }).ClickAsync();
        await Page.GetByRole(AriaRole.Option, new() { Name = "Van Dijk Transport" }).ClickAsync();
        await assign.GetByRole(AriaRole.Button, new() { Name = "Assign selected" }).ClickAsync();
        var planned = Page.GetByRole(AriaRole.List, new() { Name = "Assignment outcomes" }).GetByRole(AriaRole.Status);
        await Expect(planned).ToContainTextAsync($"Transport Order {number} is planned as Shipment");
        var shipmentNumber = (await planned.GetByRole(AriaRole.Link).TextContentAsync())!;

        await Page.GetByRole(AriaRole.Link, new() { Name = "Shipments to release" }).ClickAsync();
        var toRelease = Page.GetByRole(AriaRole.Row).Filter(new() { HasText = shipmentNumber });
        await toRelease.GetByRole(AriaRole.Checkbox, new() { Name = $"Select {shipmentNumber}" }).ClickAsync();
        await Page.GetByRole(AriaRole.Group, new() { Name = "Release" }).GetByRole(AriaRole.Button, new() { Name = "Release selected" }).ClickAsync();
        await Expect(Page.GetByRole(AriaRole.List, new() { Name = "Release outcomes" }).GetByRole(AriaRole.Status))
            .ToContainTextAsync($"Shipment {shipmentNumber} is released.");

        // The worker sends the request and reports it sent; the list says "Not yet sent" until it has.
        await Page.GetByRole(AriaRole.Link, new() { Name = "Bookings awaiting an answer" }).ClickAsync();
        var awaiting = Page.GetByRole(AriaRole.Row).Filter(new() { HasText = shipmentNumber });
        await Expect(awaiting).ToContainTextAsync("Van Dijk Transport");
        await Expect(awaiting).Not.ToContainTextAsync("Not yet sent");
        var withdraw = awaiting.GetByRole(AriaRole.Button, new() { Name = "Withdraw CB-", Exact = false });
        var bookingNumber = Regex.Match(await withdraw.GetAttributeAsync("aria-label") ?? await withdraw.InnerTextAsync(), @"CB-\d{7}").Value;
        bookingNumber.Should().NotBeEmpty();
        return (shipmentNumber, bookingNumber);
    }
}
