using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;
using Norvia.Tms.TestSupport;

namespace Norvia.Tms.Web.UiTests.PlannerAgentPlan;

/// <summary>
/// The TMS UI with the Agents switched on and Plan the list capped at two orders. Plan the list works through the whole waiting list, so these journeys have an AppHost of their own, whose list holds
/// only the orders the running test submitted (each test assigns them away when it is done): a run covers exactly those, and a third order puts a run over its cap.
/// </summary>
public sealed class PlannerAgentPlanUiFixture : TmsUiFixture
{
    private const string McpStandIn = "agent-mcp";

    protected override string[] AppHostArguments => ["Agents:Enabled=true"];

    protected override void ConfigureAppHost(IDistributedApplicationTestingBuilder builder)
    {
        builder.RemoveWhatTheCustomerServiceAgentUiTestsDontUse();
        builder.CreateResourceBuilder(builder.Resources.OfType<ProjectResource>().Single(resource => resource.Name == "tms-web"))
            .WithEnvironment("LiveList__RefreshInterval", "00:00:45")
            .WithEnvironment("Agents__MaxPlanOrders", "2");
    }

    public override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync();
        using var startup = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        await App.ResourceNotifications.WaitForResourceHealthyAsync(McpStandIn, startup.Token);
    }
}
