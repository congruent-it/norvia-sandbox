using System.Net;
using System.Text.Json.Nodes;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace Norvia.Tms.Web.UiTests.Shipments;

/// <summary>
/// The Planner notifies the Shipper of a failed delivery. Locally the Shipper notification worker plays the P7 Shipper
/// notification Logic App (ADR 0033): the notification reaches Brouwer Dranken's inbox in the Partner portal, and the
/// Exception's list shows it as Sent.
/// </summary>
public sealed class NotifyShipperJourneyTests(TmsUiFixture tms) : BrowserPage(tms)
{
    private const string VanDijkId = "CAR-0001";

    private readonly TmsUiFixture _tms = tms;

    [Fact]
    public async Task ANotificationOfAFailedDelivery_ReachesTheShippersInbox_AndEndsSent()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (shipmentNumber, bookingNumber) = await ReleaseToCarrierAsync("Van Dijk Transport");
        var reference = $"VDT-{Guid.NewGuid():N}"[..12];
        using (var confirmed = await _tms.ConfirmAsync(VanDijkId, bookingNumber, reference, cancellationToken))
        {
            confirmed.StatusCode.Should().Be(HttpStatusCode.OK, await confirmed.Content.ReadAsStringAsync(cancellationToken));
        }

        var now = DateTimeOffset.UtcNow;
        await RecordTrackingAsync("Collected", reference, now.AddMinutes(-40));
        await RecordTrackingAsync("DeliveryFailed", reference, now.AddMinutes(-5));
        var exceptionNumber = await _tms.OpenExceptionOfAsync(shipmentNumber, cancellationToken);
        exceptionNumber.Should().NotBeNull("the failed delivery opens an Exception");

        var message = $"Your delivery failed ({Guid.NewGuid():N})";
        using (var notified = await _tms.NotifyShipperAsync(exceptionNumber!, message, cancellationToken))
        {
            notified.StatusCode.Should().Be(HttpStatusCode.OK, await notified.Content.ReadAsStringAsync(cancellationToken));
        }

        // The inbox refreshes itself, so the notification shows once the worker has sent it.
        await Page.GotoAsync(new Uri(_tms.PartnerPortal, "/personas/brouwer-dranken/notifications").ToString());
        var notification = Page.GetByRole(AriaRole.Row).Filter(new() { HasText = message });
        await Expect(notification).ToBeVisibleAsync(new() { Timeout = 30_000 });
        await Expect(notification).ToContainTextAsync("Anna de Vries");
        await Expect(notification).Not.ToContainTextAsync(shipmentNumber);

        // The worker then reports it Sent, which the list of Open Exceptions shows as the last notification's status.
        var status = await LastNotificationStatusAsync(exceptionNumber!, cancellationToken);
        for (var attempt = 0; status != "Sent" && attempt < 30; attempt++)
        {
            await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
            status = await LastNotificationStatusAsync(exceptionNumber!, cancellationToken);
        }

        status.Should().Be("Sent");
    }

    private async Task<string?> LastNotificationStatusAsync(string exceptionNumber, CancellationToken cancellationToken)
    {
        using var response = await _tms.Manage.GetAsync(new Uri("/v1/exceptions/open", UriKind.Relative), cancellationToken);
        var open = JsonNode.Parse(await response.Content.ReadAsStringAsync(cancellationToken))!.AsArray();
        return open.FirstOrDefault(exception => exception?["exceptionNumber"]?.GetValue<string>() == exceptionNumber)?["lastShipperNotificationStatus"]?.GetValue<string>();
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
}
