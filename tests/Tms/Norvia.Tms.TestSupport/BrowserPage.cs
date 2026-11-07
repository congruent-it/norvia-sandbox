using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace Norvia.Tms.TestSupport;

/// <summary>
/// A fresh browser page per test, recording a trace and a video that are kept only when the test fails (coding
/// guidelines §10), under TestResults/playwright.
/// </summary>
public abstract class BrowserPage(TmsUiFixture tms) : IAsyncLifetime
{
    private const string VanDijkId = "CAR-0001";

    private static readonly string ArtifactsDirectory = Path.Combine(
        Environment.GetEnvironmentVariable("NORVIA_UI_TEST_ARTIFACTS") ?? Path.Combine(AppContext.BaseDirectory, "TestResults"),
        "playwright");

    private readonly string _videoDirectory = Path.Combine(Path.GetTempPath(), "norvia-ui-videos", Guid.NewGuid().ToString("N"));
    private IBrowserContext? _context;

    protected IPage Page { get; private set; } = null!;

    public async ValueTask InitializeAsync()
    {
        _context = await tms.Browser.NewContextAsync(new BrowserNewContextOptions
        {
            BaseURL = tms.TmsUi.ToString(),
            RecordVideoDir = _videoDirectory,
        });
        await _context.Tracing.StartAsync(new TracingStartOptions { Screenshots = true, Snapshots = true, Sources = true });
        Page = await _context.NewPageAsync();
    }

    /// <summary>
    /// Opens a view from the main navigation. A view in a group is reached by opening the group's menu first; a view that stands
    /// alone, as Customer service's do, is just clicked.
    /// </summary>
    protected async Task OpenViewAsync(string title)
    {
        var navigation = Page.GetByRole(AriaRole.Navigation, new() { Name = "Main" });
        var link = navigation.GetByRole(AriaRole.Link, new() { Name = title, Exact = true });
        if (!await link.IsVisibleAsync())
        {
            await navigation.Locator("details").Filter(new() { HasText = title }).Locator("summary").First.ClickAsync();
        }

        await link.ClickAsync();
    }

    /// <summary>Submits a booking the TMS accepts, and returns its Transport Order number.</summary>
    protected async Task<string> SubmitAcceptedAsync(JsonObject booking)
    {
        using var response = await tms.SubmitAsync(booking, TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        var accepted = await response.Content.ReadFromJsonAsync<JsonObject>(TestContext.Current.CancellationToken);
        return accepted!["transportOrderNumber"]!.GetValue<string>();
    }

    /// <summary>
    /// Plans a new Transport Order to the Carrier and releases it, and waits until its request is sent to the Carrier's inbox.
    /// Leaves the Page on the TMS's list of bookings awaiting an answer.
    /// </summary>
    protected async Task<(string ShipmentNumber, string BookingNumber)> ReleaseToCarrierAsync(string carrierName, Func<JsonObject, JsonObject>? shape = null)
    {
        var shipperReference = $"UI-{Guid.NewGuid():N}"[..20];
        var booking = Bookings.Valid(shipperReference);
        var number = await SubmitAcceptedAsync(shape is null ? booking : shape(booking));

        await Page.GotoAsync("/");
        await Page.GetByRole(AriaRole.Button, new() { Name = "Planner" }).ClickAsync();
        await OpenViewAsync("Waiting for planning");
        var order = Page.GetByRole(AriaRole.Row).Filter(new() { HasText = shipperReference });
        await order.GetByRole(AriaRole.Checkbox, new() { Name = $"Select {number}" }).ClickAsync();
        var assign = Page.GetByRole(AriaRole.Group, new() { Name = "Assign" });
        await assign.GetByRole(AriaRole.Combobox, new() { Name = "Assign to" }).ClickAsync();
        await Page.GetByRole(AriaRole.Option, new() { Name = carrierName }).ClickAsync();
        await assign.GetByRole(AriaRole.Button, new() { Name = "Assign selected" }).ClickAsync();
        var planned = Page.GetByRole(AriaRole.List, new() { Name = "Assignment outcomes" }).GetByRole(AriaRole.Status);
        await Expect(planned).ToContainTextAsync($"Transport Order {number} is planned as Shipment");
        var shipmentNumber = (await planned.GetByRole(AriaRole.Link).TextContentAsync())!;

        await OpenViewAsync("Shipments to release");
        var toRelease = Page.GetByRole(AriaRole.Row).Filter(new() { HasText = shipmentNumber });
        await toRelease.GetByRole(AriaRole.Checkbox, new() { Name = $"Select {shipmentNumber}" }).ClickAsync();
        await Page.GetByRole(AriaRole.Group, new() { Name = "Release" }).GetByRole(AriaRole.Button, new() { Name = "Release selected" }).ClickAsync();
        await Expect(Page.GetByRole(AriaRole.List, new() { Name = "Release outcomes" }).GetByRole(AriaRole.Status))
            .ToContainTextAsync($"Shipment {shipmentNumber} is released.");

        await OpenViewAsync("Bookings awaiting an answer");
        var awaiting = Page.GetByRole(AriaRole.Row).Filter(new() { HasText = shipmentNumber });
        await Expect(awaiting).ToContainTextAsync(carrierName);
        var withdraw = awaiting.GetByRole(AriaRole.Button, new() { Name = "Withdraw CB-", Exact = false });
        var bookingNumber = Regex.Match(await withdraw.GetAttributeAsync("aria-label") ?? await withdraw.InnerTextAsync(), @"CB-\d{7}").Value;
        bookingNumber.Should().NotBeEmpty();
        return (shipmentNumber, bookingNumber);
    }

    /// <summary>Releases a new Transport Order to Van Dijk Transport, has it confirm the booking and report the Shipment In transit.</summary>
    protected async Task<(string ShipmentNumber, string Reference)> InTransitShipmentAsync(Func<JsonObject, JsonObject>? shape = null, TimeSpan? startedAgo = null)
    {
        var (shipmentNumber, bookingNumber) = await ReleaseToCarrierAsync("Van Dijk Transport", shape);
        var reference = $"VDT-{Guid.NewGuid():N}"[..12];
        using (var confirmed = await tms.ConfirmAsync(VanDijkId, bookingNumber, reference, TestContext.Current.CancellationToken))
        {
            confirmed.StatusCode.Should().Be(HttpStatusCode.OK, await confirmed.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        }

        var now = DateTimeOffset.UtcNow - (startedAgo ?? TimeSpan.Zero);
        await RecordRotterdamTrackingAsync("Collected", reference, now.AddMinutes(-40));
        await RecordRotterdamTrackingAsync("Departed", reference, now.AddMinutes(-30));
        return (shipmentNumber, reference);
    }

    /// <summary>Records a Tracking event from Van Dijk Transport, reported from Rotterdam, for the booking reference.</summary>
    protected async Task RecordRotterdamTrackingAsync(string type, string reference, DateTimeOffset eventTime)
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
        using var response = await tms.RecordTrackingAsync(trackingEvent, TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    public async ValueTask DisposeAsync()
    {
        GC.SuppressFinalize(this);
        if (_context is null)
        {
            return;
        }

        var failed = TestContext.Current.TestState?.Result == TestResult.Failed;
        var name = string.Concat((TestContext.Current.Test?.TestDisplayName ?? "test").Split(Path.GetInvalidFileNameChars()));
        await _context.Tracing.StopAsync(failed ? new TracingStopOptions { Path = Path.Combine(ArtifactsDirectory, $"{name}.trace.zip") } : new());
        var video = Page.Video;
        await _context.DisposeAsync();

        if (failed && video is not null)
        {
            await video.SaveAsAsync(Path.Combine(ArtifactsDirectory, $"{name}.webm"));
        }

        if (Directory.Exists(_videoDirectory))
        {
            Directory.Delete(_videoDirectory, recursive: true);
        }
    }
}
