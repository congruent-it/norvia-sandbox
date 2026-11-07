using Microsoft.Playwright;

namespace Norvia.Erp.TestSupport;

/// <summary>
/// A fresh browser page per test, on the ERP UI. It records a trace and a video that are kept only when the test fails (coding guidelines §10),
/// under TestResults/playwright (or <c>NORVIA_UI_TEST_ARTIFACTS</c>). A test project's own base class prepares the ERP in <see cref="PrepareAsync"/>.
/// </summary>
public abstract class TracedPage(IBrowser browser, Uri erpUi) : IAsyncLifetime
{
    private static readonly string ArtifactsDirectory = Path.Combine(
        Environment.GetEnvironmentVariable("NORVIA_UI_TEST_ARTIFACTS") ?? Path.Combine(AppContext.BaseDirectory, "TestResults"),
        "playwright");

    private readonly string _videoDirectory = Path.Combine(Path.GetTempPath(), "norvia-ui-videos", Guid.NewGuid().ToString("N"));
    private IBrowserContext? _context;

    protected static CancellationToken Ct => TestContext.Current.CancellationToken;

    protected IPage Page { get; private set; } = null!;

    /// <summary>Runs before the page opens, for what a test starts from.</summary>
    protected virtual Task PrepareAsync() => Task.CompletedTask;

    public async ValueTask InitializeAsync()
    {
        await PrepareAsync();
        _context = await browser.NewContextAsync(new BrowserNewContextOptions
        {
            BaseURL = erpUi.ToString(),
            RecordVideoDir = _videoDirectory,
        });
        await _context.Tracing.StartAsync(new TracingStartOptions { Screenshots = true, Snapshots = true, Sources = true });
        Page = await _context.NewPageAsync();
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
