using System.Security.Cryptography;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;
using Microsoft.Playwright;
using Norvia.AppHost;

namespace Norvia.PartnerPortal.Web.UiTests.IntakeAgent;

/// <summary>
/// The Partner portal with the Agents switched on (<c>Agents:Enabled</c>), as a developer runs it: the local Partner API runs the Intake agent in process against the scripted fake model, which needs no
/// Foundry, and a booking still travels the whole path to the TMS. The estate is cut down to what that path needs. A headless Chromium drives the portal. <see cref="StopPartnerApiAsync"/> takes the
/// Partner API away to show an Agent that fails.
/// </summary>
public sealed class IntakeAgentUiFixture : IAsyncLifetime
{
    private const string PartnerApi = "partner-api";

    private static readonly TimeSpan StartupTimeout = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan CommandTimeout = TimeSpan.FromMinutes(2);

    // What the tests don't use: the Simulator and the Logic App workers a booking does not pass through. The Driver app stays: the portal reads its address (Faults), so removing it would leave the portal waiting for ever. The booking worker plays the intake Logic App and stays, and so does the Carrier invoice worker, which the Partner API waits for.
    private static readonly string[] Unneeded =
    [
        "dbgate",
        "simulator", "simulator-migrations", "simulator-web",
        "shipper-notification-worker", "carrier-cancellation-worker", "trip-dispatch-worker", "driver-reports-worker", "tracking-worker", "proof-of-delivery-worker",
        "billing-worker", "carrier-cost-worker", "shipper-invoice-worker", "carrier-invoice-outcome-worker",
    ];

    private DistributedApplication? _app;
    private IPlaywright? _playwright;

    /// <summary>The Partner portal, where the presenter plays a Shipper Persona.</summary>
    public Uri PartnerPortal { get; private set; } = null!;

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
            [$"Parameters:{LocalTokens.SigningKeyParameter}={Convert.ToHexString(RandomNumberGenerator.GetBytes(32))}", "Agents:Enabled=true"],
            startup.Token);
        foreach (var resource in builder.Resources
            .Where(resource => Unneeded.Any(name => resource.Name == name || resource.Name == $"{name}-rebuilder"))
            .ToList())
        {
            builder.Resources.Remove(resource);
        }

        _app = await builder.BuildAsync(startup.Token);
        await _app.StartAsync(startup.Token);
        foreach (var resource in new[] { "tms-api", PartnerApi, "partner-portal-web" })
        {
            await _app.ResourceNotifications.WaitForResourceHealthyAsync(resource, startup.Token);
        }

        await _app.ResourceNotifications.WaitForResourceAsync("booking-worker", KnownResourceStates.Running, startup.Token);

        PartnerPortal = _app.GetEndpoint("partner-portal-web", "http");
        _playwright = await Playwright.CreateAsync();
        Browser = await _playwright.Chromium.LaunchAsync();
    }

    /// <summary>Stops the Partner API, so the Intake agent is out of reach. Start it again with <see cref="StartPartnerApiAsync"/>; every test on this fixture sees it stopped meanwhile.</summary>
    public async Task StopPartnerApiAsync(CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(CommandTimeout);
        // Aspire may report a failed stop while the process is shutting down gracefully, so the result is not checked: what counts is that it is down.
        _ = await _app!.ResourceCommands.ExecuteCommandAsync(PartnerApi, KnownResourceCommands.StopCommand, timeout.Token);
        // What a stopped project reports varies (Exited, Finished, even Unknown); it is down once it is none of these.
        string[] up = [KnownResourceStates.Starting, KnownResourceStates.Running, KnownResourceStates.Stopping, KnownResourceStates.Waiting];
        await _app.ResourceNotifications.WaitForResourceAsync(
            PartnerApi, resource => resource.Snapshot.State?.Text is { } state && !up.Contains(state), timeout.Token);
    }

    public async Task StartPartnerApiAsync(CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(CommandTimeout);
        var result = await _app!.ResourceCommands.ExecuteCommandAsync(PartnerApi, KnownResourceCommands.StartCommand, timeout.Token);
        result.Success.Should().BeTrue(result.Message);
        await _app.ResourceNotifications.WaitForResourceHealthyAsync(PartnerApi, timeout.Token);
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

        GC.SuppressFinalize(this);
    }
}
