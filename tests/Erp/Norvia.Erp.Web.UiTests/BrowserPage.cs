using System.Globalization;
using Microsoft.Playwright;

namespace Norvia.Erp.Web.UiTests;

/// <summary>
/// A fresh browser page per test, on the ERP UI, after the ERP has been emptied of what earlier tests recorded. It records a trace and
/// a video that are kept only when the test fails (coding guidelines §10), under TestResults/playwright.
/// </summary>
public abstract class BrowserPage(ErpUiFixture erp) : IAsyncLifetime
{
    protected const string Brouwer = "SHP-0001";
    protected const string BrouwerName = "Brouwer Dranken";

    private static readonly string ArtifactsDirectory = Path.Combine(
        Environment.GetEnvironmentVariable("NORVIA_UI_TEST_ARTIFACTS") ?? Path.Combine(AppContext.BaseDirectory, "TestResults"),
        "playwright");

    private readonly string _videoDirectory = Path.Combine(Path.GetTempPath(), "norvia-ui-videos", Guid.NewGuid().ToString("N"));
    private IBrowserContext? _context;

    protected static CancellationToken Ct => TestContext.Current.CancellationToken;

    protected ErpUiFixture Erp { get; } = erp;

    protected IPage Page { get; private set; } = null!;

    public async ValueTask InitializeAsync()
    {
        await Erp.ResetAsync(Ct);
        _context = await Erp.Browser.NewContextAsync(new BrowserNewContextOptions
        {
            BaseURL = Erp.ErpUi.ToString(),
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

    /// <summary>A Transport Order number that looks like the TMS's (TO- and seven digits), and is not one a test recorded before it.</summary>
    protected static string NewTransportOrderNumber() => string.Create(CultureInfo.InvariantCulture, $"TO-{Random.Shared.Next(1_000_000, 10_000_000)}");

    /// <summary>Records a Billable line for a Shipper, as the Billing Logic App does, and returns its Transport Order number.</summary>
    protected async Task<string> RecordLineAsync(string shipperId = Brouwer, decimal freight = 100m, decimal minimumChargeAdjustment = 0m)
    {
        var number = NewTransportOrderNumber();
        await Erp.RecordBillableLineAsync(number, shipperId, freight, minimumChargeAdjustment, Ct);
        return number;
    }

    /// <summary>Opens a view: the section in the main navigation, then the view in the strip of that section's views below it.</summary>
    protected async Task OpenViewAsync(string section, string view)
    {
        await Page.GetByRole(AriaRole.Navigation, new() { Name = "Main" }).GetByRole(AriaRole.Link, new() { Name = section, Exact = true }).ClickAsync();
        await Page.GetByRole(AriaRole.Navigation, new() { Name = section, Exact = true }).GetByRole(AriaRole.Link, new() { Name = view, Exact = true }).ClickAsync();
    }
}
