using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace Norvia.PartnerPortal.Web.UiTests.Bookings;

/// <summary>
/// The as-found fault of the TMS that the Shipper sees (P8), switched on from the presenter panel and seen on the Shipper's tracking screen. It belongs to the booking journeys' class, which runs its
/// tests one at a time: the fault changes every Shipper's tracking, so no other journey may run while it is on. The Driver app's faults and the TMS unavailable fault are in the TMS UI tests.
/// </summary>
public sealed partial class BookingJourneyTests
{
    private const string ShipperTrackingApiFailing = "tms-shipper-tracking-api-failing";

    [Fact]
    public async Task WhileTheShipperTrackingApiFails_TheShippersTrackingScreenShowsTheError_AndTheTransportOrderIsBackOnceItIsSwitchedOff()
    {
        var reference = NewReference();
        await OpenBookingFormAsync("Brouwer Dranken");
        await FillAsync("Booking", "Shipper reference", reference);
        await BookAsync();
        var number = await AcceptedNumberAsync();
        var tracking = new Uri(_norvia.PartnerPortal, $"/personas/brouwer-dranken/tracking/{number}").ToString();
        await SwitchFaultOnAsync(ShipperTrackingApiFailing, "2 minutes");
        try
        {
            // API Management passes the TMS's 503 on, so the Shipper sees the error at once, and no Transport Order.
            await Portal.GotoAsync(tracking);
            await Expect(Portal.GetByRole(AriaRole.Heading, new() { Name = number, Level = 1 })).ToBeVisibleAsync();
            var error = Portal.GetByRole(AriaRole.Alert).Filter(new() { HasText = "Tracking is temporarily unavailable. Try again shortly." });
            await Expect(error).ToBeVisibleAsync(new() { Timeout = PatienceMilliseconds });
            await Expect(Portal.GetByRole(AriaRole.Main)).Not.ToContainTextAsync("Tracking status");
        }
        finally
        {
            await SwitchFaultOffAsync(ShipperTrackingApiFailing);
        }

        await Portal.GotoAsync(tracking);
        await Expect(Portal.GetByRole(AriaRole.Main)).ToContainTextAsync("Received", new() { Timeout = PatienceMilliseconds });
        await Expect(Portal.GetByRole(AriaRole.Alert).Filter(new() { HasText = "Tracking is temporarily unavailable" })).ToHaveCountAsync(0);
    }
}
