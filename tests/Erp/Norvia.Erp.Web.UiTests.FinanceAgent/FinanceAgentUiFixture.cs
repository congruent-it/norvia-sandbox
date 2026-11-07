using System.Security.Cryptography;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;
using Microsoft.Playwright;
using Norvia.AppHost;
using Norvia.Erp.TestSupport;

namespace Norvia.Erp.Web.UiTests.FinanceAgent;

/// <summary>
/// The ERP UI with the Agents switched on (<c>Agents:Enabled</c>), as a developer runs it: the MCP stand-in that plays API Management's MCP server serves the Carrier invoice matching agent's
/// tool, and the ERP UI asks the scripted fake model, which needs no Foundry. The estate is cut down to the ERP (API, UI and database), the stand-in and what it waits for. The tests record the
/// Carrier costs and Carrier invoices they need through the ERP API (<see cref="CarrierInvoiceCalls"/>), as the Carrier cost and Carrier invoice Logic Apps do, under numbers of their own, with tokens signed with the key the
/// AppHost trusts. A headless Chromium drives the ERP UI. <see cref="StopAgentAsync"/> takes the stand-in away to show an Agent that fails.
/// </summary>
public sealed class FinanceAgentUiFixture : IAsyncLifetime
{
    private const string McpStandIn = "agent-mcp";

    private static readonly TimeSpan StartupTimeout = TimeSpan.FromMinutes(8);
    private static readonly TimeSpan CommandTimeout = TimeSpan.FromMinutes(2);

    // What the tests don't use: the other applications' resources and the Logic App workers. The ERP, the TMS (which the stand-in waits for), the Estate health check and the
    // Integration console (the same), SQL Server and the Service Bus emulator stay.
    private static readonly string[] Unneeded =
    [
        "dbgate", "partner-portal", "partner-portal-migrations", "partner-portal-web", "partner-api",
        "driver-app-migrations", "driver-app-api", "driver-app-web",
        "simulator", "simulator-migrations", "simulator-web",
        "booking-worker", "shipper-notification-worker", "carrier-cancellation-worker", "trip-dispatch-worker", "driver-reports-worker", "tracking-worker", "proof-of-delivery-worker",
        "billing-worker", "carrier-cost-worker", "shipper-invoice-worker", "carrier-invoice-outcome-worker", "carrier-invoice-worker",
    ];

    private readonly string _tokenSigningKey = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
    private DistributedApplication? _app;
    private IPlaywright? _playwright;

    /// <summary>The ERP UI, Finance's application.</summary>
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
            [$"Parameters:{LocalTokens.SigningKeyParameter}={_tokenSigningKey}", "Agents:Enabled=true"],
            startup.Token);
        foreach (var resource in builder.Resources
            .Where(resource => Unneeded.Any(name => resource.Name == name || resource.Name == $"{name}-rebuilder"))
            .ToList())
        {
            builder.Resources.Remove(resource);
        }

        _app = await builder.BuildAsync(startup.Token);
        await _app.StartAsync(startup.Token);
        foreach (var resource in new[] { "erp-api", "erp-web", McpStandIn })
        {
            await _app.ResourceNotifications.WaitForResourceHealthyAsync(resource, startup.Token);
        }

        ErpUi = _app.GetEndpoint("erp-web", "http");
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

        GC.SuppressFinalize(this);
    }

    /// <summary>Stops the MCP stand-in, so the Agent's tool is out of reach. Start it again with <see cref="StartAgentAsync"/>; every test on this fixture sees it stopped meanwhile.</summary>
    public async Task StopAgentAsync(CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(CommandTimeout);
        var result = await _app!.ResourceCommands.ExecuteCommandAsync(McpStandIn, KnownResourceCommands.StopCommand, timeout.Token);
        result.Success.Should().BeTrue(result.Message);
        await _app.ResourceNotifications.WaitForResourceAsync(McpStandIn, [KnownResourceStates.Finished, KnownResourceStates.Exited], timeout.Token);
    }

    public async Task StartAgentAsync(CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(CommandTimeout);
        var result = await _app!.ResourceCommands.ExecuteCommandAsync(McpStandIn, KnownResourceCommands.StartCommand, timeout.Token);
        result.Success.Should().BeTrue(result.Message);
        await _app.ResourceNotifications.WaitForResourceHealthyAsync(McpStandIn, timeout.Token);
    }

    /// <summary>A client of the ERP API that sends the token a Logic App holding <paramref name="role"/> would, such as the Carrier cost and Carrier invoice Logic Apps.</summary>
    public HttpClient LogicAppClient(string role) => CarrierInvoiceCalls.Authorize(_app!.CreateHttpClient("erp-api"), _tokenSigningKey, role);
}
