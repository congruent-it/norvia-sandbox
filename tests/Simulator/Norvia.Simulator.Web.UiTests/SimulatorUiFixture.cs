using System.Security.Cryptography;
using Aspire.Hosting;
using Aspire.Hosting.Testing;
using Microsoft.Playwright;
using Norvia.AppHost;

[assembly: AssemblyFixture(typeof(Norvia.Simulator.Web.UiTests.SimulatorUiFixture))]

namespace Norvia.Simulator.Web.UiTests;

/// <summary>
/// Runs the Norvia AppHost once for the whole test run, cut down to what a booking needs: SQL Server, the Service Bus emulator, the TMS and
/// its migrations, the local Partner API, the Integration console (the log's Message trail links point at it) and the Simulator with its
/// database. A headless Chromium drives the Simulator, which sends its bookings to the real Partner API.
/// </summary>
public sealed class SimulatorUiFixture : IAsyncLifetime
{
    // What the booking path doesn't use only costs startup time and memory.
    private static readonly string[] Unneeded =
    [
        "dbgate", "tms-web", "partner-portal", "partner-portal-migrations", "partner-portal-web", "driver-app-migrations", "driver-app-api", "driver-app-web",
        "booking-worker", "shipper-notification-worker", "carrier-cancellation-worker", "trip-dispatch-worker", "driver-reports-worker",
        "tracking-worker", "proof-of-delivery-worker",
    ];

    private static readonly TimeSpan StartupTimeout = TimeSpan.FromMinutes(5);

    private DistributedApplication? _app;
    private IPlaywright? _playwright;

    public Uri Address { get; private set; } = null!;

    public IBrowser Browser { get; private set; } = null!;

    public async ValueTask InitializeAsync()
    {
        using var startup = new CancellationTokenSource(StartupTimeout);

        // Installs Chromium the first time; CI installs it beforehand with its system dependencies.
        if (Microsoft.Playwright.Program.Main(["install", "chromium"]) != 0)
        {
            throw new InvalidOperationException("Installing Chromium for Playwright failed.");
        }

        var builder = await DistributedApplicationTestingBuilder.CreateAsync<Projects.Norvia_AppHost>(
            [$"Parameters:{LocalTokens.SigningKeyParameter}={Convert.ToHexString(RandomNumberGenerator.GetBytes(32))}"],
            startup.Token);
        foreach (var resource in builder.Resources
            .Where(resource => Unneeded.Any(name => resource.Name == name || resource.Name == $"{name}-rebuilder"))
            .ToList())
        {
            builder.Resources.Remove(resource);
        }

        _app = await builder.BuildAsync(startup.Token);
        await _app.StartAsync(startup.Token);
        foreach (var resource in new[] { "tms-api", "partner-api", "simulator-web" })
        {
            await _app.ResourceNotifications.WaitForResourceHealthyAsync(resource, startup.Token);
        }

        Address = _app.GetEndpoint("simulator-web", "http");

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

        if (_app is not null)
        {
            await _app.DisposeAsync();
        }
    }
}
