using Microsoft.Playwright;

[assembly: AssemblyFixture(typeof(Norvia.IntegrationConsole.Web.UiTests.ConsoleUiFixture))]

namespace Norvia.IntegrationConsole.Web.UiTests;

/// <summary>Runs the console once for the whole test run, on the <see cref="ConsoleWebHost"/>, and a headless Chromium to drive it.</summary>
public sealed class ConsoleUiFixture : IAsyncLifetime
{
    private IPlaywright? _playwright;

    public ConsoleWebHost Console { get; } = new();

    /// <summary>The same console with <c>Agents:Enabled</c> on.</summary>
    public ConsoleWebHost AgentsConsole { get; } = new(agentsEnabled: true);

    public IBrowser Browser { get; private set; } = null!;

    public async ValueTask InitializeAsync()
    {
        // Installs Chromium the first time; CI installs it beforehand with its system dependencies.
        if (Microsoft.Playwright.Program.Main(["install", "chromium"]) != 0)
        {
            throw new InvalidOperationException("Installing Chromium for Playwright failed.");
        }

        Console.StartServer();
        AgentsConsole.StartServer();

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
        await Console.DisposeAsync();
        await AgentsConsole.DisposeAsync();
    }
}
