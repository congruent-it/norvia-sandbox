using System.Net;
using System.Text.Json.Nodes;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace Norvia.Tms.Web.UiTests.Shipments;

/// <summary>
/// A Carrier Shipment is booked and then followed by the Carrier's Tracking events: the Planner sees it in Shipments in
/// progress, sees each event arrive without a refresh, opens its timeline, and sees it leave the list once Delivered.
/// </summary>
public sealed class ShipmentsInProgressJourneyTests(TmsUiFixture tms) : BrowserPage(tms)
{
    private const string VanDijkId = "CAR-0001";

    private readonly TmsUiFixture _tms = tms;

    [Fact]
    public async Task ABookedShipment_FollowedByTrackingEvents_ShowsEachLiveHasATimeline_AndLeavesTheListWhenDelivered()
    {
        var (shipmentNumber, bookingNumber) = await ReleaseToCarrierAsync("Van Dijk Transport");
        var reference = $"VDT-{Guid.NewGuid():N}"[..12];
        await ConfirmAsync(bookingNumber, reference);

        await Page.GetByRole(AriaRole.Link, new() { Name = "Shipments in progress" }).ClickAsync();
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Shipments in progress", Level = 1 })).ToBeVisibleAsync();
        var shipment = Page.GetByRole(AriaRole.Row).Filter(new() { HasText = shipmentNumber });
        await Expect(shipment).ToContainTextAsync("Van Dijk Transport");
        await Expect(shipment).ToContainTextAsync("Booked");
        await Expect(shipment).ToContainTextAsync("None yet");

        var now = DateTimeOffset.UtcNow;
        await RecordAsync("Collected", reference, now.AddMinutes(-30));
        await Expect(shipment).ToContainTextAsync("Collected, ");
        await Expect(shipment).ToContainTextAsync("Rotterdam");

        await RecordAsync("Departed", reference, now.AddMinutes(-20));
        await Expect(shipment).ToContainTextAsync("In transit");

        await shipment.GetByRole(AriaRole.Link, new() { Name = shipmentNumber }).ClickAsync();
        var timeline = Page.GetByRole(AriaRole.Region, new() { Name = "Tracking timeline" });
        var rows = timeline.GetByRole(AriaRole.Row);
        await Expect(rows).ToHaveCountAsync(3);
        await Expect(rows.Nth(1)).ToContainTextAsync("Collected");
        await Expect(rows.Nth(2)).ToContainTextAsync("Departed");
        await Expect(rows.Nth(2)).ToContainTextAsync("Yes");

        await Page.GetByRole(AriaRole.Link, new() { Name = "Shipments in progress" }).ClickAsync();
        await Expect(shipment).ToBeVisibleAsync();
        await RecordAsync("Delivered", reference, now.AddMinutes(-10));
        await Expect(shipment).ToHaveCountAsync(0);
    }

    private async Task ConfirmAsync(string bookingNumber, string reference)
    {
        using var response = await _tms.ConfirmAsync(VanDijkId, bookingNumber, reference, TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    private async Task RecordAsync(string type, string reference, DateTimeOffset eventTime)
    {
        var trackingEvent = new JsonObject
        {
            ["eventId"] = Guid.NewGuid().ToString(),
            ["source"] = new JsonObject { ["type"] = "Carrier", ["carrierId"] = VanDijkId },
            ["carrierBookingReference"] = reference,
            ["type"] = type,
            ["eventTime"] = JsonValue.Create(eventTime),
            ["location"] = new JsonObject { ["place"] = "Rotterdam", ["postcode"] = "3011 AA", ["countryCode"] = "NL" },
        };
        using var response = await _tms.RecordTrackingAsync(trackingEvent, TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }
}
