using Microsoft.Playwright;

[assembly: AssemblyFixture(typeof(Norvia.HealthCheck.Web.UiTests.HealthCheckUiFixture))]

namespace Norvia.HealthCheck.Web.UiTests;

/// <summary>Runs a headless Chromium once for the whole test run; each test brings the app it drives.</summary>
public sealed class HealthCheckUiFixture : IAsyncLifetime
{
    private IPlaywright? _playwright;

    public IBrowser Browser { get; private set; } = null!;

    public async ValueTask InitializeAsync()
    {
        // Installs Chromium the first time; CI installs it beforehand with its system dependencies.
        if (Microsoft.Playwright.Program.Main(["install", "chromium"]) != 0)
        {
            throw new InvalidOperationException("Installing Chromium for Playwright failed.");
        }

        _playwright = await Playwright.CreateAsync();
        Browser = await _playwright.Chromium.LaunchAsync();
    }

    public async ValueTask DisposeAsync()
    {
        if (Browser is not null)
        {
            await Browser.DisposeAsync();
        }

        _playwright?.Dispose();
    }
}
