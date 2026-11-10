using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;
using Norvia.Tms.TestSupport;

namespace Norvia.Tms.Web.UiTests.PlannerAgent;

/// <summary>
/// The TMS UI with the Agents switched on (<c>Agents:Enabled</c>), as a developer runs it: the MCP stand-in that plays API Management's MCP server serves the Planner agent's
/// tool, and the TMS UI asks the scripted fake model, which needs no Foundry. <see cref="StopAgentAsync"/> takes the stand-in away to show an Agent that fails.
/// </summary>
public sealed class PlannerAgentUiFixture : TmsUiFixture
{
    private const string McpStandIn = "agent-mcp";
    private static readonly TimeSpan CommandTimeout = TimeSpan.FromMinutes(2);

    protected override string[] AppHostArguments => ["Agents:Enabled=true"];

    protected override void ConfigureAppHost(IDistributedApplicationTestingBuilder builder) =>
        builder.RemoveWhatTheCustomerServiceAgentUiTestsDontUse();

    public override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync();
        using var startup = new CancellationTokenSource(CommandTimeout);
        await App.ResourceNotifications.WaitForResourceHealthyAsync(McpStandIn, startup.Token);
    }

    /// <summary>Stops the MCP stand-in, so the Agent's tool is out of reach. Start it again with <see cref="StartAgentAsync"/>; every test on this fixture sees it stopped meanwhile.</summary>
    public async Task StopAgentAsync()
    {
        using var timeout = new CancellationTokenSource(CommandTimeout);
        var result = await App.ResourceCommands.ExecuteCommandAsync(McpStandIn, KnownResourceCommands.StopCommand, timeout.Token);
        result.Success.Should().BeTrue(result.Message);
        await App.ResourceNotifications.WaitForResourceAsync(McpStandIn, [KnownResourceStates.Finished, KnownResourceStates.Exited], timeout.Token);
    }

    public async Task StartAgentAsync()
    {
        using var timeout = new CancellationTokenSource(CommandTimeout);
        var result = await App.ResourceCommands.ExecuteCommandAsync(McpStandIn, KnownResourceCommands.StartCommand, timeout.Token);
        result.Success.Should().BeTrue(result.Message);
        await App.ResourceNotifications.WaitForResourceHealthyAsync(McpStandIn, timeout.Token);
    }
}
