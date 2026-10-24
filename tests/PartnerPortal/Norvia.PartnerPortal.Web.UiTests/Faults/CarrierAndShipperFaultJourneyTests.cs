using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace Norvia.PartnerPortal.Web.UiTests.Bookings;

/// <summary>
/// The as-found faults of the outside parties (P4, P5 and P7), switched on from the presenter panel and seen on the screens a presenter shows beside it. Each journey starts from a
/// Shipment of its own and switches its fault off again, even when it fails. They belong to the booking journeys' class, which runs its tests one at a time: a fault changes every
/// Carrier or Shipper, so no other journey may run while one is on. The messages, the dead-letter queue and the Message trail are covered by the integration tests.
/// </summary>
public sealed partial class BookingJourneyTests
{
    private const string TrackingUnmappedCode = "carrier-tracking-unmapped-code";
    private const string BookingEndpointDown = "carrier-booking-endpoint-down";
    private const string ShipperNotificationEndpointDown = "shipper-notification-endpoint-down";

    // The booking Logic App retries the Carrier's endpoint for about 35 seconds before it reports the booking Not delivered; the fault must outlast that.
    private const float RetriesMilliseconds = 120_000;

    [Fact]
    public async Task AnUnmappedDeliveredCode_IsAcceptedForTheCarrier_AndTheShippersScreenStaysInTransit()
    {
        var released = await ReleasedBookingAsync();
        var (bookingNumber, _) = await ConfirmedBookingAsync(released.ShipmentNumber);
        await ReportAsync(bookingNumber, "Collected (PU)", "Departed (DEP)");
        await SwitchFaultOnAsync(TrackingUnmappedCode, "2 minutes");
        try
        {
            await ReportAsync(bookingNumber, "Delivered (POD)");

            // The Carrier is told it succeeded: Norvia answers 202 and the failure is further on, in the Logic App's map.
            var notice = Portal.GetByRole(AriaRole.Status).Filter(new() { HasText = $"Norvia accepted Delivered (POD) for {bookingNumber}" });
            await Expect(notice).ToBeVisibleAsync();
            await Expect(notice).ToContainTextAsync("It tells you no more");

            await Portal.GotoAsync(new Uri(_norvia.PartnerPortal, $"/personas/brouwer-dranken/tracking/{released.TransportOrderNumber}").ToString());
            await Expect(Portal.GetByRole(AriaRole.Heading, new() { Name = released.TransportOrderNumber, Level = 1 })).ToBeVisibleAsync();
            await Expect(Portal.GetByRole(AriaRole.Main)).ToContainTextAsync("In transit", new() { Timeout = PatienceMilliseconds });
            await Expect(Portal.GetByRole(AriaRole.Main)).Not.ToContainTextAsync("Delivered");
        }
        finally
        {
            await SwitchFaultOffAsync(TrackingUnmappedCode);
        }
    }

    [Fact]
    public async Task ADownBookingEndpoint_LeavesTheBookingNotDelivered_ForThePlannerToRelease()
    {
        await SwitchFaultOnAsync(BookingEndpointDown, "2 minutes");
        try
        {
            var released = await ReleasedBookingAsync();

            // The booking is first awaiting an answer, and once the retries are used up it leaves that list.
            await OpenTmsViewAsync("Bookings awaiting an answer");
            var awaiting = Tms.GetByRole(AriaRole.Row).Filter(new() { HasText = released.ShipmentNumber });
            await Expect(awaiting).ToContainTextAsync(VanDijkName);
            await Expect(awaiting).ToHaveCountAsync(0, new() { Timeout = RetriesMilliseconds });

            // The Shipment is Planned again, and the Planner reads why it must be released again.
            await OpenTmsViewAsync("Shipments to release");
            var toRelease = Tms.GetByRole(AriaRole.Row).Filter(new() { HasText = released.ShipmentNumber });
            await Expect(toRelease).ToContainTextAsync($"Not delivered to {VanDijkName}");

            // Nothing reached the Carrier's inbox.
            await Portal.GotoAsync(new Uri(_norvia.PartnerPortal, $"/personas/{VanDijk}/booking-requests").ToString());
            await Expect(Portal.GetByRole(AriaRole.Heading, new() { Name = "Booking requests", Level = 1 })).ToBeVisibleAsync();
            await Expect(Portal.GetByRole(AriaRole.Row).Filter(new() { HasText = released.ShipmentNumber })).ToHaveCountAsync(0);
        }
        finally
        {
            await SwitchFaultOffAsync(BookingEndpointDown);
        }
    }

    [Fact]
    public async Task ADownShipperEndpoint_LeavesTheNotificationNotDelivered_OnTheOpenExceptions()
    {
        var released = await ReleasedBookingAsync();
        var (bookingNumber, _) = await ConfirmedBookingAsync(released.ShipmentNumber);
        await ReportAsync(bookingNumber, "Collected (PU)");
        // The failed delivery is only taken in once the TMS has the Shipment collected.
        await Tms.GotoAsync(new Uri(_norvia.TmsUi, "/shipments/in-progress").ToString());
        await Expect(Tms.GetByRole(AriaRole.Row).Filter(new() { HasText = released.ShipmentNumber })).ToContainTextAsync("Collected", new() { Timeout = PatienceMilliseconds });
        await ReportAsync(bookingNumber, "Delivery failed (NDL)");
        await SwitchFaultOnAsync(ShipperNotificationEndpointDown, "2 minutes");
        try
        {
            await Tms.GotoAsync(new Uri(_norvia.TmsUi, "/exceptions").ToString());
            var exception = Tms.GetByRole(AriaRole.Row).Filter(new() { HasText = released.ShipmentNumber });
            await Expect(exception).ToBeVisibleAsync(new() { Timeout = PatienceMilliseconds });
            await Expect(exception).ToContainTextAsync("Not notified");
            await exception.GetByRole(AriaRole.Button, new() { NameRegex = new(@"^Notify Shipper of ") }).ClickAsync();
            await Tms.GetByRole(AriaRole.Textbox, new() { Name = "Message to the Shipper" }).FillAsync($"Your delivery failed ({Guid.NewGuid():N})");
            await Tms.GetByRole(AriaRole.Button, new() { Name = "Send notification" }).ClickAsync();

            // The worker retries the Shipper's endpoint, then reports the notification Not delivered, which the list shows.
            await Expect(exception).ToContainTextAsync("Not delivered", new() { Timeout = RetriesMilliseconds });
        }
        finally
        {
            await SwitchFaultOffAsync(ShipperNotificationEndpointDown);
        }
    }

    // The Carrier Persona reports its Shipment, event by event, from its Shipments page.
    private async Task ReportAsync(string bookingNumber, params string[] events)
    {
        await Portal.GotoAsync(new Uri(_norvia.PartnerPortal, $"/personas/{VanDijk}/shipments").ToString());
        var shipment = Portal.GetByRole(AriaRole.Row).Filter(new() { HasText = bookingNumber });
        foreach (var @event in events)
        {
            await shipment.GetByRole(AriaRole.Button, new() { Name = $"{@event} for {bookingNumber}", Exact = true }).ClickAsync(new() { Timeout = PatienceMilliseconds });
        }
    }

    // Starts from a fault that is off, wherever an earlier failed run left it.
    private async Task SwitchFaultOnAsync(string fault, string duration)
    {
        var group = await OpenFaultAsync(fault);
        var off = group.GetByRole(AriaRole.Button, new() { Name = $"Switch off {fault}" });
        if (await off.IsEnabledAsync())
        {
            await off.ClickAsync();
            await Expect(off).ToBeDisabledAsync();
        }

        // The duration is a Fluent UI select, a custom element: open it and pick the option.
        var select = group.GetByRole(AriaRole.Combobox, new() { Name = "Duration" });
        await select.ClickAsync();
        await Portal.GetByRole(AriaRole.Option, new() { Name = duration, Exact = true }).ClickAsync();
        await Expect(select).ToHaveAttributeAsync("aria-expanded", "false");
        await group.GetByRole(AriaRole.Button, new() { Name = $"Switch on {fault}" }).ClickAsync();
        await Expect(group.GetByRole(AriaRole.Status)).ToContainTextAsync("On");
    }

    private async Task SwitchFaultOffAsync(string fault)
    {
        var group = await OpenFaultAsync(fault);
        var off = group.GetByRole(AriaRole.Button, new() { Name = $"Switch off {fault}" });
        if (await off.IsEnabledAsync())
        {
            await off.ClickAsync();
        }

        await Expect(off).ToBeDisabledAsync();
    }

    private async Task<ILocator> OpenFaultAsync(string fault)
    {
        await Portal.GotoAsync(new Uri(_norvia.PartnerPortal, "/presenter").ToString());
        await Expect(Portal.GetByRole(AriaRole.Heading, new() { Name = "Presenter panel", Level = 1 })).ToBeVisibleAsync();
        var group = Portal.GetByRole(AriaRole.Group, new() { Name = fault, Exact = true });
        await Expect(group).ToBeVisibleAsync(new() { Timeout = 30_000 });
        return group;
    }
}
