using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace Norvia.PartnerPortal.Web.UiTests.IntakeAgent;

/// <summary>
/// A fresh browser context per test with the Partner portal in it, and the steps every journey shares. It records a trace and a video that are kept only when the test fails (coding guidelines §10),
/// under TestResults/playwright.
/// </summary>
public abstract class BrowserPage(IntakeAgentUiFixture norvia) : IAsyncLifetime
{
    private static readonly string ArtifactsDirectory = Path.Combine(
        Environment.GetEnvironmentVariable("NORVIA_UI_TEST_ARTIFACTS") ?? Path.Combine(AppContext.BaseDirectory, "TestResults"),
        "playwright");

    private readonly string _videoDirectory = Path.Combine(Path.GetTempPath(), "norvia-ui-videos", Guid.NewGuid().ToString("N"));
    private IBrowserContext? _context;

    protected IntakeAgentUiFixture Norvia { get; } = norvia;

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

    /// <summary>Plays the Shipper Persona and opens "Fill from an email" from its booking form.</summary>
    protected async Task OpenFillFromEmailAsync(string persona = "Brouwer Dranken")
    {
        await Portal.GotoAsync(Norvia.PartnerPortal.ToString());
        await Portal.GetByRole(AriaRole.Form, new() { Name = "Persona" }).GetByRole(AriaRole.Button, new() { Name = persona }).ClickAsync();
        // The form isn't prerendered, so once its heading shows, it is interactive.
        await Expect(Portal.GetByRole(AriaRole.Heading, new() { Name = "Book a transport", Level = 1 })).ToBeVisibleAsync();
        await Portal.GetByRole(AriaRole.Link, new() { Name = "Fill from an email" }).ClickAsync();
        await Expect(Portal.GetByRole(AriaRole.Heading, new() { Name = "Fill from an email", Level = 1 })).ToBeVisibleAsync();
        await Expect(Portal.GetByRole(AriaRole.Main)).ToContainTextAsync($"As {persona}");
    }

    /// <summary>The longest the portal waits for the Partner API before it gives up (PartnerApi:Timeout, 90 seconds), and a little more.</summary>
    protected const float PartnerApiGivesUp = 120_000;

    /// <summary>Pastes <paramref name="email"/> into the text area.</summary>
    protected async Task PasteEmailAsync(string email)
    {
        var text = Portal.GetByRole(AriaRole.Textbox, new() { Name = "The email's text" });
        await text.FillAsync(email);
        // Blazor binds the text on change, which the text area raises when it loses focus.
        await text.BlurAsync();
    }

    /// <summary>Pastes <paramref name="email"/> and asks the assistant to fill the booking.</summary>
    protected async Task PasteAndFillAsync(string email)
    {
        await PasteEmailAsync(email);
        await Portal.GetByRole(AriaRole.Button, new() { Name = "Fill the booking" }).ClickAsync();
    }

    protected ILocator Field(string group, string label) =>
        Portal.GetByRole(AriaRole.Group, new() { Name = group, Exact = true }).GetByLabel(label, new() { Exact = true });

    protected ILocator Answer() => Portal.GetByRole(AriaRole.Region, new() { Name = "Norvia's answer" });

    /// <summary>The warning above the prefilled form that lists what the email did not give.</summary>
    protected ILocator Flags() => Portal.GetByRole(AriaRole.Status).Filter(new() { HasText = "The Intake agent filled in what the email gives" });

    // Unique per test run and short, as Norvia keeps each Shipper reference once per Shipper.
    protected static string NewReference() => $"UI-{Guid.NewGuid():N}"[..20];
}
