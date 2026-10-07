using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace Norvia.PartnerPortal.Web.UiTests.Bookings;

/// <summary>
/// The Shipper's tracking journey (P8): book a transport, then follow it on the tracking screen through API Management's
/// stand-in to the TMS. It belongs to the booking journeys' class, which runs its tests one at a time, as one takes the
/// TMS down.
/// </summary>
public sealed partial class BookingJourneyTests
{
    [Fact]
    public async Task ABookedTransportOrder_AppearsOnTheShippersTrackingScreen_AsReceived_AndOpensItsDetailFromASearch()
    {
        var reference = NewReference();
        await OpenBookingFormAsync("Brouwer Dranken");
        await FillAsync("Booking", "Shipper reference", reference);
        await BookAsync();
        var number = await AcceptedNumberAsync();

        await Portal.GetByRole(AriaRole.Navigation, new() { Name = "Main" }).GetByRole(AriaRole.Link, new() { Name = "Tracking" }).ClickAsync();

        await Expect(Portal.GetByRole(AriaRole.Heading, new() { Name = "Tracking", Level = 1 })).ToBeVisibleAsync();
        var row = Portal.GetByRole(AriaRole.Row).Filter(new() { HasText = reference });
        await Expect(row).ToContainTextAsync(number, new() { Timeout = 30_000 });
        await Expect(row).ToContainTextAsync("Received");
        await Expect(row).ToContainTextAsync("Rotterdam, NL");

        await Portal.GetByLabel("Transport Order number or Shipper reference", new() { Exact = true }).FillAsync(reference);
        await Portal.GetByRole(AriaRole.Button, new() { Name = "Search" }).ClickAsync();

        await Expect(Portal.GetByRole(AriaRole.Heading, new() { Name = number, Level = 1 })).ToBeVisibleAsync();
        await Expect(Portal.GetByRole(AriaRole.Main)).ToContainTextAsync(reference);
        await Expect(Portal.GetByRole(AriaRole.Main)).ToContainTextAsync("Received");
        await Expect(Portal.GetByRole(AriaRole.Main)).ToContainTextAsync("There is no Proof of delivery yet.");
        await Expect(Portal.GetByText("Fetched at")).ToBeVisibleAsync();
    }

    [Fact]
    public async Task TheTrackingScreen_FiltersOnTrackingStatus()
    {
        var reference = NewReference();
        await OpenBookingFormAsync("Brouwer Dranken");
        await FillAsync("Booking", "Shipper reference", reference);
        await BookAsync();
        await AcceptedNumberAsync();
        await Portal.GetByRole(AriaRole.Navigation, new() { Name = "Main" }).GetByRole(AriaRole.Link, new() { Name = "Tracking" }).ClickAsync();
        var row = Portal.GetByRole(AriaRole.Row).Filter(new() { HasText = reference });
        await Expect(row).ToBeVisibleAsync(new() { Timeout = 30_000 });

        await Portal.GetByLabel("Tracking status", new() { Exact = true }).SelectOptionAsync("Delivered");

        await Expect(row).ToBeHiddenAsync();
    }
}
