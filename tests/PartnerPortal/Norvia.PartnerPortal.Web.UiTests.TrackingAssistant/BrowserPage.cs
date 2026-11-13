using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace Norvia.PartnerPortal.Web.UiTests.TrackingAssistant;

/// <summary>
/// A fresh browser context per test, and the steps every journey shares: enter a Transport Order for the Shipper, play its Persona and open the order's tracking page. It records a trace and a video that are
/// kept only when the test fails (coding guidelines §10), under TestResults/playwright.
/// </summary>
public abstract class BrowserPage(TrackingAssistantUiFixture norvia) : IAsyncLifetime
{
    private static readonly string ArtifactsDirectory = Path.Combine(
        Environment.GetEnvironmentVariable("NORVIA_UI_TEST_ARTIFACTS") ?? Path.Combine(AppContext.BaseDirectory, "TestResults"),
        "playwright");

    /// <summary>The Shipper Persona that asks: Brouwer Dranken, Shipper SHP-0001.</summary>
    private const string Persona = "Brouwer Dranken";

    private const string ShipperId = "SHP-0001";

    private readonly string _videoDirectory = Path.Combine(Path.GetTempPath(), "norvia-ui-videos", Guid.NewGuid().ToString("N"));
    private IBrowserContext? _context;

    protected TrackingAssistantUiFixture Norvia { get; } = norvia;

    protected static CancellationToken Ct => TestContext.Current.CancellationToken;

    protected IPage Portal { get; private set; } = null!;

    public async ValueTask InitializeAsync()
    {
        _context = await Norvia.Browser.NewContextAsync(new BrowserNewContextOptions { RecordVideoDir = _videoDirectory });
        await _context.Tracing.StartAsync(new TracingStartOptions { Screenshots = true, Snapshots = true, Sources = true });
        Portal = await _context.NewPageAsync();
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
        var video = Portal.Video;
        await _context.DisposeAsync();

        if (failed && video is not null)
        {
            await video.SaveAsAsync(Path.Combine(ArtifactsDirectory, $"{name}.portal.webm"));
        }

        if (Directory.Exists(_videoDirectory))
        {
            Directory.Delete(_videoDirectory, recursive: true);
        }
    }

    /// <summary>The conversation's answers (<c>assistant-answer</c>) and notices (<c>assistant-notice</c>), in the order they were shown.</summary>
    protected ILocator Conversation() => Portal.GetByRole(AriaRole.List, new() { Name = "Conversation" });

    protected ILocator Answers() => Conversation().GetByTestId("assistant-answer");

    protected ILocator Notices() => Conversation().GetByTestId("assistant-notice");

    /// <summary>Enters a Transport Order for the Shipper, plays its Persona and opens the order's tracking page. Returns the order's number.</summary>
    protected async Task<string> OpenOrderAsync(Uri portal)
    {
        var reference = NewReference();
        var number = await Norvia.Tms.EnterTransportOrderAsync(ShipperId, reference, Ct);

        await Portal.GotoAsync(portal.ToString());
        await Portal.GetByRole(AriaRole.Form, new() { Name = "Persona" }).GetByRole(AriaRole.Button, new() { Name = Persona }).ClickAsync();
        await Portal.GetByRole(AriaRole.Navigation, new() { Name = "Main" }).GetByRole(AriaRole.Link, new() { Name = "Tracking" }).ClickAsync();
        await Expect(Portal.GetByRole(AriaRole.Heading, new() { Name = "Tracking", Level = 1 })).ToBeVisibleAsync();
        await Portal.GetByLabel("Transport Order number or Shipper reference", new() { Exact = true }).FillAsync(reference);
        await Portal.GetByRole(AriaRole.Button, new() { Name = "Search" }).ClickAsync();
        await Expect(Portal.GetByRole(AriaRole.Heading, new() { Name = number, Level = 1 })).ToBeVisibleAsync();
        await Expect(Portal.GetByRole(AriaRole.Heading, new() { Name = "Ask about your orders", Level = 2 })).ToBeVisibleAsync();
        return number;
    }

    /// <summary>Types the question into the panel and asks it.</summary>
    protected async Task AskAsync(string question)
    {
        await Portal.GetByLabel("Your question", new() { Exact = true }).FillAsync(question);
        await Portal.GetByRole(AriaRole.Button, new() { Name = "Ask", Exact = true }).ClickAsync();
    }

    // Unique per test run and short, as Norvia keeps each Shipper reference once per Shipper.
    private static string NewReference() => $"UI-{Guid.NewGuid():N}"[..20];
}
