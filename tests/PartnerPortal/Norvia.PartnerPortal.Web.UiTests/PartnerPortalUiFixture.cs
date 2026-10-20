using System.Security.Cryptography;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;
using Microsoft.Playwright;
using Norvia.AppHost;

[assembly: AssemblyFixture(typeof(Norvia.PartnerPortal.Web.UiTests.PartnerPortalUiFixture))]

namespace Norvia.PartnerPortal.Web.UiTests;

/// <summary>
/// Runs the Norvia AppHost once for the whole test run: the Partner portal, the local Partner API that stands in for API
/// Management and the intake Logic App (ADR 0023), the TMS with its UI, SQL and the Service Bus emulator. A booking in
/// the portal travels the whole path to the TMS. A headless Chromium drives both UIs.
/// </summary>
public sealed class PartnerPortalUiFixture : IAsyncLifetime
{
    private const string CarrierCostWorker = "carrier-cost-worker";

    private static readonly TimeSpan StartupTimeout = TimeSpan.FromMinutes(5);

    private DistributedApplication? _app;
    private IPlaywright? _playwright;

    public Uri PartnerPortal { get; private set; } = null!;

    public Uri TmsUi { get; private set; } = null!;

    /// <summary>The ERP UI, where Finance decides the Carrier invoices.</summary>
    public Uri ErpUi { get; private set; } = null!;

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
        _app = await builder.BuildAsync(startup.Token);
        await _app.StartAsync(startup.Token);
        foreach (var resource in new[] { "tms-api", "tms-web", "partner-api", "partner-portal-web", "erp-web", "erp-api" })
        {
            await _app.ResourceNotifications.WaitForResourceHealthyAsync(resource, startup.Token);
        }

        // A delivered Carrier Shipment's cost reaches the ERP, a Carrier invoice reaches it and its outcome reaches the portal through the workers that play those Logic Apps.
        foreach (var worker in new[] { "booking-worker", CarrierCostWorker, "carrier-invoice-worker", "carrier-invoice-outcome-worker" })
        {
            await _app.ResourceNotifications.WaitForResourceAsync(worker, KnownResourceStates.Running, startup.Token);
        }

        PartnerPortal = _app.GetEndpoint("partner-portal-web", "http");
        TmsUi = _app.GetEndpoint("tms-web", "http");
        ErpUi = _app.GetEndpoint("erp-web", "http");

        _playwright = await Playwright.CreateAsync();
        Browser = await _playwright.Chromium.LaunchAsync();
    }

    /// <summary>Stops the TMS, so Norvia can't take a booking in, until <see cref="StartTmsAsync"/>.</summary>
    public async Task StopTmsAsync(CancellationToken cancellationToken)
    {
        using var stopping = WithinTwoMinutes(cancellationToken);
        await _app!.ResourceCommands.ExecuteCommandAsync("tms-api", KnownResourceCommands.StopCommand, stopping.Token);
        // What a stopped project reports varies (Exited, Finished, even Unknown); it is down once it is none of these.
        string[] up = [KnownResourceStates.Starting, KnownResourceStates.Running, KnownResourceStates.Stopping, KnownResourceStates.Waiting];
        await _app.ResourceNotifications.WaitForResourceAsync(
            "tms-api", resource => resource.Snapshot.State?.Text is { } state && !up.Contains(state), stopping.Token);
    }

    public async Task StartTmsAsync(CancellationToken cancellationToken)
    {
        using var starting = WithinTwoMinutes(cancellationToken);
        await _app!.ResourceCommands.ExecuteCommandAsync("tms-api", KnownResourceCommands.StartCommand, starting.Token);
        await _app.ResourceNotifications.WaitForResourceHealthyAsync("tms-api", starting.Token);
    }

    /// <summary>
    /// Stops the worker that plays the Carrier cost Logic App, so no Carrier cost reaches the ERP until <see cref="StartCarrierCostWorkerAsync"/>:
    /// a Carrier invoice then arrives before the Carrier cost it refers to, and its line is an Unknown booking.
    /// </summary>
    public async Task StopCarrierCostWorkerAsync(CancellationToken cancellationToken)
    {
        using var stopping = WithinTwoMinutes(cancellationToken);
        await _app!.ResourceCommands.ExecuteCommandAsync(CarrierCostWorker, KnownResourceCommands.StopCommand, stopping.Token);
        // What a stopped project reports varies (Exited, Finished, even Unknown); it is down once it is none of these.
        string[] up = [KnownResourceStates.Starting, KnownResourceStates.Running, KnownResourceStates.Stopping, KnownResourceStates.Waiting];
        await _app.ResourceNotifications.WaitForResourceAsync(
            CarrierCostWorker, resource => resource.Snapshot.State?.Text is { } state && !up.Contains(state), stopping.Token);
    }

    public async Task StartCarrierCostWorkerAsync(CancellationToken cancellationToken)
    {
        using var starting = WithinTwoMinutes(cancellationToken);
        await _app!.ResourceCommands.ExecuteCommandAsync(CarrierCostWorker, KnownResourceCommands.StartCommand, starting.Token);
        await _app.ResourceNotifications.WaitForResourceAsync(CarrierCostWorker, KnownResourceStates.Running, starting.Token);
    }

    // A resource that never gets there fails the test, rather than hanging the run.
    private static CancellationTokenSource WithinTwoMinutes(CancellationToken cancellationToken)
    {
        var within = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        within.CancelAfter(TimeSpan.FromMinutes(2));
        return within;
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
