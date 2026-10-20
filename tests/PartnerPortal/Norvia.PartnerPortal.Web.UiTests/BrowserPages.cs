using Microsoft.Playwright;

namespace Norvia.PartnerPortal.Web.UiTests;

/// <summary>
/// A fresh browser context per test, with the Partner portal and the TMS UI side by side, as a presenter shows them. It
/// records a trace and a video that are kept only when the test fails (coding guidelines §10), under
/// TestResults/playwright.
/// </summary>
public abstract class BrowserPages(PartnerPortalUiFixture norvia) : IAsyncLifetime
{
    private static readonly string ArtifactsDirectory = Path.Combine(
        Environment.GetEnvironmentVariable("NORVIA_UI_TEST_ARTIFACTS") ?? Path.Combine(AppContext.BaseDirectory, "TestResults"),
        "playwright");

    private readonly string _videoDirectory = Path.Combine(Path.GetTempPath(), "norvia-ui-videos", Guid.NewGuid().ToString("N"));
    private IBrowserContext? _context;

    /// <summary>The Partner portal, where the presenter plays a Persona.</summary>
    protected IPage Portal { get; private set; } = null!;

    /// <summary>The TMS UI, beside it.</summary>
    protected IPage Tms { get; private set; } = null!;

    /// <summary>The ERP UI, where Finance works.</summary>
    protected IPage Erp { get; private set; } = null!;

    public async ValueTask InitializeAsync()
    {
        _context = await norvia.Browser.NewContextAsync(new BrowserNewContextOptions { RecordVideoDir = _videoDirectory });
        await _context.Tracing.StartAsync(new TracingStartOptions { Screenshots = true, Snapshots = true, Sources = true });
        Portal = await _context.NewPageAsync();
        Tms = await _context.NewPageAsync();
        Erp = await _context.NewPageAsync();
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
        var videos = new[] { Portal.Video, Tms.Video, Erp.Video };
        await _context.DisposeAsync();

        if (failed)
        {
            foreach (var (video, page) in videos.Zip(["portal", "tms", "erp"]))
            {
                if (video is not null)
                {
                    await video.SaveAsAsync(Path.Combine(ArtifactsDirectory, $"{name}.{page}.webm"));
                }
            }
        }

        if (Directory.Exists(_videoDirectory))
        {
            Directory.Delete(_videoDirectory, recursive: true);
        }
    }
}
