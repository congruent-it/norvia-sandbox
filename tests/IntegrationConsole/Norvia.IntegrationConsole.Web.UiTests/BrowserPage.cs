using System.Text;
using System.Text.Json;
using Microsoft.Playwright;

namespace Norvia.IntegrationConsole.Web.UiTests;

/// <summary>
/// A fresh browser page per test, signed in as the test says, recording a trace and a video that are kept only when the
/// test fails (coding guidelines §10), under TestResults/playwright.
/// </summary>
public abstract class BrowserPage(ConsoleUiFixture console) : IAsyncLifetime
{
    private static readonly string ArtifactsDirectory = Path.Combine(
        Environment.GetEnvironmentVariable("NORVIA_UI_TEST_ARTIFACTS") ?? Path.Combine(AppContext.BaseDirectory, "TestResults"),
        "playwright");

    private readonly string _videoDirectory = Path.Combine(Path.GetTempPath(), "norvia-ui-videos", Guid.NewGuid().ToString("N"));
    private IBrowserContext? _context;

    protected IPage Page { get; private set; } = null!;

    protected ConsoleWebHost Console => console.Console;

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    /// <summary>Opens a page as Easy Auth would pass the operator on after they signed in with Entra, holding these app roles.</summary>
    protected async Task SignInAsync(string name, params string[] roles)
    {
        _context = await console.Browser.NewContextAsync(new BrowserNewContextOptions
        {
            BaseURL = console.Console.Address.ToString(),
            RecordVideoDir = _videoDirectory,
            ExtraHTTPHeaders = new Dictionary<string, string> { ["X-MS-CLIENT-PRINCIPAL"] = ClientPrincipal(name, roles) },
        });
        await _context.Tracing.StartAsync(new TracingStartOptions { Screenshots = true, Snapshots = true, Sources = true });
        Page = await _context.NewPageAsync();
    }

    /// <summary>The titles of the table's columns that don't fit their header and show cut off.</summary>
    protected static Task<string[]> CutOffColumnTitlesAsync(ILocator table) =>
        table.EvaluateAsync<string[]>("""
            table => [...table.querySelectorAll('[role=columnheader], th')]
                .filter(header => [header, ...header.querySelectorAll('*')].some(element => element.scrollWidth > element.clientWidth + 1
                    && getComputedStyle(element).textOverflow === 'ellipsis'))
                .map(header => header.textContent.trim())
            """);

    private static string ClientPrincipal(string name, string[] roles) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
        {
            auth_typ = "aad",
            name_typ = "name",
            role_typ = "http://schemas.microsoft.com/ws/2008/06/identity/claims/role",
            claims = roles.Select(role => new { typ = "roles", val = role }).Prepend(new { typ = "name", val = name }),
        })));

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
