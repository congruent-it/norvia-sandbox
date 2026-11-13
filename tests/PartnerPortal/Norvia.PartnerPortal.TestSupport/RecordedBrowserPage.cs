using Microsoft.Playwright;

namespace Norvia.PartnerPortal.TestSupport;

/// <summary>
/// A fresh browser context per test with the Partner portal's page in it. It records a trace and a video that are kept only when the test fails (coding guidelines §10), under TestResults/playwright.
/// </summary>
public abstract class RecordedBrowserPage(IBrowser browser) : IAsyncLifetime
{
    private static readonly string ArtifactsDirectory = Path.Combine(
        Environment.GetEnvironmentVariable("NORVIA_UI_TEST_ARTIFACTS") ?? Path.Combine(AppContext.BaseDirectory, "TestResults"),
        "playwright");

    private readonly string _videoDirectory = Path.Combine(Path.GetTempPath(), "norvia-ui-videos", Guid.NewGuid().ToString("N"));
    private IBrowserContext? _context;

    protected static CancellationToken Ct => TestContext.Current.CancellationToken;

    protected IPage Portal { get; private set; } = null!;

    public async ValueTask InitializeAsync()
    {
        _context = await browser.NewContextAsync(new BrowserNewContextOptions { RecordVideoDir = _videoDirectory });
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
}
