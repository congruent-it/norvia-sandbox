using Microsoft.Playwright;

namespace Norvia.HealthCheck.Web.UiTests;

/// <summary>
/// A fresh app and browser page per test, recording a trace and a video that are kept only when the test fails (coding guidelines §10),
/// under TestResults/playwright.
/// </summary>
public abstract class BrowserPage(HealthCheckUiFixture fixture) : IAsyncLifetime
{
    private static readonly string ArtifactsDirectory = Path.Combine(
        Environment.GetEnvironmentVariable("NORVIA_UI_TEST_ARTIFACTS") ?? Path.Combine(AppContext.BaseDirectory, "TestResults"),
        "playwright");

    private readonly string _videoDirectory = Path.Combine(Path.GetTempPath(), "norvia-ui-videos", Guid.NewGuid().ToString("N"));
    private HealthCheckWebHost? _app;
    private IBrowserContext? _context;

    protected IPage Page { get; private set; } = null!;

    /// <summary>The app under test, once <see cref="StartAsync"/> has started it.</summary>
    protected HealthCheckWebHost App => _app ?? throw new InvalidOperationException("Start the app first.");

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    /// <summary>Starts an app reading this snapshot, with the local operator holding these app roles, and opens a page on it.</summary>
    protected Task StartAsync(string snapshot, params string[] roles) => StartAsync(new HealthCheckWebHost(snapshot, roles));

    /// <summary>Starts an app reading this modelled spend, with the local operator holding these app roles, and opens a page on it.</summary>
    protected Task StartWithSpendAsync(string costSnapshot, params string[] roles) =>
        StartAsync(new HealthCheckWebHost("fixed", costSnapshot, roles));

    private async Task StartAsync(HealthCheckWebHost app)
    {
        _app = app;
        _context = await fixture.Browser.NewContextAsync(new BrowserNewContextOptions
        {
            BaseURL = _app.Address.ToString(),
            RecordVideoDir = _videoDirectory,
        });
        await _context.Tracing.StartAsync(new TracingStartOptions { Screenshots = true, Snapshots = true, Sources = true });
        Page = await _context.NewPageAsync();
    }

    public async ValueTask DisposeAsync()
    {
        GC.SuppressFinalize(this);
        if (_context is not null)
        {
            var failed = TestContext.Current.TestState?.Result == TestResult.Failed;
            var name = string.Concat((TestContext.Current.Test?.TestDisplayName ?? "test").Split(Path.GetInvalidFileNameChars()));
            await _context.Tracing.StopAsync(failed ? new TracingStopOptions { Path = Path.Combine(ArtifactsDirectory, $"{name}.trace.zip") } : new());
            var video = Page.Video;
            await _context.DisposeAsync();

            if (failed && video is not null)
            {
                await video.SaveAsAsync(Path.Combine(ArtifactsDirectory, $"{name}.webm"));
            }
        }

        if (_app is not null)
        {
            await _app.DisposeAsync();
        }

        if (Directory.Exists(_videoDirectory))
        {
            Directory.Delete(_videoDirectory, recursive: true);
        }
    }
}
