using System.Globalization;
using System.Net;
using System.Text.Json.Nodes;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace Norvia.Tms.Web.UiTests.Exceptions;

/// <summary>
/// The Planner works Exceptions from the TMS UI (P7): the list of Open Exceptions with its markers and only the actions that are
/// allowed, each action and the resolution, the cancellation that comes back in Shipments to release, the marker in Shipments in
/// progress and the Shipment's timeline.
/// </summary>
public sealed class OpenExceptionsJourneyTests(TmsUiFixture tms) : BrowserPage(tms)
{
    private const string VanDijkId = "CAR-0001";

    private readonly TmsUiFixture _tms = tms;

    [Fact]
    public async Task AFailedDelivery_ShowsInOpenExceptions_WithOnlyTheActionsAllowedAfterCollection_AndEachActionAndTheResolutionWork()
    {
        var (shipmentNumber, exceptionNumber) = await ShipmentWithFailedDeliveryAsync();

        await Page.GetByRole(AriaRole.Link, new() { Name = "Open Exceptions", Exact = true }).ClickAsync();
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Open Exceptions", Level = 1 })).ToBeVisibleAsync();
        var exception = Page.GetByRole(AriaRole.Row).Filter(new() { HasText = exceptionNumber });
        await Expect(exception).ToContainTextAsync(shipmentNumber);
        await Expect(exception).ToContainTextAsync("Brouwer Dranken");
        await Expect(exception).ToContainTextAsync("Van Dijk Transport");
        await Expect(exception).ToContainTextAsync("Delivery failed");
        await Expect(exception).ToContainTextAsync("Not notified");
        await Expect(exception.GetByRole(AriaRole.Button, new() { Name = $"Notify Shipper of {exceptionNumber}" })).ToBeVisibleAsync();
        await Expect(exception.GetByRole(AriaRole.Button, new() { Name = $"Agree delivery window for {exceptionNumber}" })).ToBeVisibleAsync();
        await Expect(exception.GetByRole(AriaRole.Button, new() { Name = $"Add note to {exceptionNumber}" })).ToBeVisibleAsync();
        await Expect(exception.GetByRole(AriaRole.Button, new() { Name = $"Resolve {exceptionNumber}" })).ToBeVisibleAsync();
        // Collected already, so the Carrier booking can't be cancelled.
        await Expect(exception.GetByRole(AriaRole.Button, new() { Name = "Cancel Carrier booking" })).ToHaveCountAsync(0);

        // Notify the Shipper: the message starts from the template, which the Planner edits.
        await exception.GetByRole(AriaRole.Button, new() { Name = $"Notify Shipper of {exceptionNumber}" }).ClickAsync();
        var notify = Page.GetByRole(AriaRole.Group, new() { Name = $"Notify Shipper of {exceptionNumber}" });
        var message = notify.Locator("textarea");
        await Expect(message).Not.ToHaveValueAsync("");
        var written = $"We will call you about your failed delivery ({Guid.NewGuid():N}).";
        await message.FillAsync(written);
        await notify.GetByRole(AriaRole.Button, new() { Name = "Send notification" }).ClickAsync();
        await Expect(Page.GetByRole(AriaRole.Status)).ToContainTextAsync($"The Shipper is being notified of {exceptionNumber}.");
        // The list reads again by itself, and the worker that plays the Logic App reports the notification Sent.
        await Expect(exception).ToContainTextAsync("Sent", new() { Timeout = 30_000 });

        // Add a note.
        await exception.GetByRole(AriaRole.Button, new() { Name = $"Add note to {exceptionNumber}" }).ClickAsync();
        var note = Page.GetByRole(AriaRole.Group, new() { Name = $"Add note to {exceptionNumber}" });
        await note.Locator("textarea").FillAsync("Phoned the Consignee, redelivery Thursday");
        await note.GetByRole(AriaRole.Button, new() { Name = "Add note", Exact = true }).ClickAsync();
        await Expect(exception).ToContainTextAsync("Phoned the Consignee, redelivery Thursday");

        // Agree a delivery window.
        await exception.GetByRole(AriaRole.Button, new() { Name = $"Agree delivery window for {exceptionNumber}" }).ClickAsync();
        var window = Page.GetByRole(AriaRole.Group, new() { Name = $"Agree delivery window for {exceptionNumber}" });
        await window.Locator("input").Nth(0).FillAsync(RotterdamIn(TimeSpan.FromDays(2)));
        await window.Locator("input").Nth(1).FillAsync(RotterdamIn(TimeSpan.FromDays(2).Add(TimeSpan.FromHours(4))));
        await window.GetByRole(AriaRole.Button, new() { Name = "Agree window" }).ClickAsync();
        await Expect(Page.GetByRole(AriaRole.Status)).ToContainTextAsync($"The delivery window for {exceptionNumber} is agreed.");

        // The marker on the Shipments in progress.
        await Page.GetByRole(AriaRole.Link, new() { Name = "Shipments in progress" }).ClickAsync();
        await Expect(Page.GetByRole(AriaRole.Row).Filter(new() { HasText = shipmentNumber })).ToContainTextAsync("Open Exception");

        // The timeline shows the Exception with its cause, note and notification, and the agreed window.
        await Page.GotoAsync($"/shipments/{shipmentNumber}");
        var story = Page.GetByRole(AriaRole.Article, new() { Name = $"Exception {exceptionNumber}" });
        await Expect(story).ToContainTextAsync("Delivery failed");
        await Expect(story).ToContainTextAsync("Cause: Tracking event");
        await Expect(story).ToContainTextAsync("Phoned the Consignee, redelivery Thursday");
        await Expect(story).ToContainTextAsync(written);
        await Expect(Page.GetByRole(AriaRole.Region, new() { Name = "Agreed delivery windows" }).GetByRole(AriaRole.Row).Filter(new() { HasText = exceptionNumber }))
            .ToContainTextAsync("Current");

        // Resolve it: the Exception leaves the list and stays on the timeline with its outcome.
        await Page.GotoAsync("/exceptions");
        await Page.GetByRole(AriaRole.Button, new() { Name = $"Resolve {exceptionNumber}" }).ClickAsync();
        var resolve = Page.GetByRole(AriaRole.Group, new() { Name = $"Resolve {exceptionNumber}" });
        await resolve.GetByRole(AriaRole.Combobox, new() { Name = "Outcome" }).ClickAsync();
        await Page.GetByRole(AriaRole.Option, new() { Name = "Redelivered" }).ClickAsync();
        await resolve.Locator("textarea").FillAsync("Delivered on Thursday");
        await resolve.GetByRole(AriaRole.Button, new() { Name = "Resolve Exception" }).ClickAsync();
        await Expect(Page.GetByRole(AriaRole.Status)).ToContainTextAsync($"{exceptionNumber} is resolved.");
        await Expect(Page.GetByRole(AriaRole.Row).Filter(new() { HasText = exceptionNumber })).ToHaveCountAsync(0);

        await Page.GotoAsync($"/shipments/{shipmentNumber}");
        var resolved = Page.GetByRole(AriaRole.Article, new() { Name = $"Exception {exceptionNumber}" });
        await Expect(resolved).ToContainTextAsync("Resolved: Redelivered");
        await Expect(resolved).ToContainTextAsync("Delivered on Thursday");
    }

    [Fact]
    public async Task ACollectionFailedBeforeCollection_OffersCancellingTheCarrierBooking_WhichComesBackInShipmentsToRelease()
    {
        var (shipmentNumber, bookingNumber) = await ReleaseToCarrierAsync("Van Dijk Transport");
        var reference = await ConfirmAsync(bookingNumber);
        await RecordTrackingAsync("CollectionFailed", reference, DateTimeOffset.UtcNow.AddMinutes(-5));
        var exceptionNumber = await OpenExceptionOfAsync(shipmentNumber);

        await Page.GotoAsync("/exceptions");
        var exception = Page.GetByRole(AriaRole.Row).Filter(new() { HasText = exceptionNumber });
        await Expect(exception).ToContainTextAsync("Collection failed");
        // Not collected yet, so no delivery window can be agreed.
        await Expect(exception.GetByRole(AriaRole.Button, new() { Name = $"Agree delivery window for {exceptionNumber}" })).ToHaveCountAsync(0);
        await exception.GetByRole(AriaRole.Button, new() { Name = $"Cancel Carrier booking of {exceptionNumber}" }).ClickAsync();
        var cancel = Page.GetByRole(AriaRole.Group, new() { Name = $"Cancel Carrier booking of {exceptionNumber}" });
        await cancel.GetByRole(AriaRole.Button, new() { Name = "Cancel Carrier booking" }).ClickAsync();
        await Expect(cancel).ToContainTextAsync("Give the reason for cancelling the Carrier booking.");
        await cancel.Locator("textarea").FillAsync("Van Dijk can't collect today");
        await cancel.GetByRole(AriaRole.Button, new() { Name = "Cancel Carrier booking" }).ClickAsync();
        await Expect(Page.GetByRole(AriaRole.Status)).ToContainTextAsync($"Shipment {shipmentNumber} is back in the Shipments to release");

        // The Exception stays open, but no longer offers cancelling: the Shipment is Planned again.
        await Expect(exception.GetByRole(AriaRole.Button, new() { Name = $"Cancel Carrier booking of {exceptionNumber}" })).ToHaveCountAsync(0);

        await Page.GetByRole(AriaRole.Link, new() { Name = "Shipments to release" }).ClickAsync();
        var toRelease = Page.GetByRole(AriaRole.Row).Filter(new() { HasText = shipmentNumber });
        await Expect(toRelease).ToContainTextAsync("Cancelled by Norvia: Van Dijk can't collect today");
        // The worker that plays the Carrier cancellation Logic App tells the Carrier, so it isn't flagged.
        await Expect(toRelease).Not.ToContainTextAsync("Carrier not told");

        await Page.GotoAsync($"/shipments/{shipmentNumber}");
        await Expect(Page.GetByRole(AriaRole.Article, new() { Name = $"Exception {exceptionNumber}" })).ToContainTextAsync("cancelled");
    }

    private async Task<(string ShipmentNumber, string ExceptionNumber)> ShipmentWithFailedDeliveryAsync()
    {
        var (shipmentNumber, bookingNumber) = await ReleaseToCarrierAsync("Van Dijk Transport");
        var reference = await ConfirmAsync(bookingNumber);
        var now = DateTimeOffset.UtcNow;
        await RecordTrackingAsync("Collected", reference, now.AddMinutes(-40));
        await RecordTrackingAsync("DeliveryFailed", reference, now.AddMinutes(-5));
        return (shipmentNumber, await OpenExceptionOfAsync(shipmentNumber));
    }

    private async Task<string> ConfirmAsync(string bookingNumber)
    {
        var reference = $"VDT-{Guid.NewGuid():N}"[..12];
        using var confirmed = await _tms.ConfirmAsync(VanDijkId, bookingNumber, reference, TestContext.Current.CancellationToken);
        confirmed.StatusCode.Should().Be(HttpStatusCode.OK, await confirmed.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        return reference;
    }

    private async Task<string> OpenExceptionOfAsync(string shipmentNumber)
    {
        var number = await _tms.OpenExceptionOfAsync(shipmentNumber, TestContext.Current.CancellationToken);
        number.Should().NotBeNull($"the Tracking event opens an Exception for {shipmentNumber}");
        return number!;
    }

    private async Task RecordTrackingAsync(string type, string reference, DateTimeOffset eventTime)
    {
        var trackingEvent = new JsonObject
        {
            ["eventId"] = Guid.NewGuid().ToString(),
            ["source"] = new JsonObject { ["type"] = "Carrier", ["carrierId"] = VanDijkId },
            ["carrierBookingReference"] = reference,
            ["type"] = type,
            ["eventTime"] = JsonValue.Create(eventTime),
            ["location"] = new JsonObject { ["place"] = "Düsseldorf", ["postcode"] = "40213", ["countryCode"] = "DE" },
        };
        using var response = await _tms.RecordTrackingAsync(trackingEvent, TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    // A time ahead of now as the Planner types it into the form: Rotterdam's local time.
    private static string RotterdamIn(TimeSpan ahead) =>
        TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow.Add(ahead), TimeZoneInfo.FindSystemTimeZoneById("Europe/Amsterdam"))
            .ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
}
