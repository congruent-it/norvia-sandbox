using Norvia.Agents.Definitions;
using Norvia.Agents.Tools;
using Norvia.Tms.Api.Agents;
using Norvia.Tms.Application.Agents;
using Norvia.Tms.Application.Agents.Planner;

namespace Norvia.Tms.ArchitectureTests;

/// <summary>
/// The Planner notification agent (#444, ADR 0011, ADR 0040): the boundary is a tool, not an instruction. It is a separate definition and
/// identity with exactly two read tools, the Notify Shipper template and the Shipper view, behind a role of its own, so it can reach no
/// other Agent's tools, no other identity can reach its, and no tool can write or send.
/// </summary>
public sealed class PlannerNotificationAgentRulesTests
{
    private static readonly string[] OtherTmsAgents = ["customer-service", "customer-service-reply", "planner"];

    private static AgentDefinition Definition(string name) =>
        AgentDefinitionLoader.Parse(File.ReadAllText(Path.Combine(RepositoryRoot.Path, "agents", $"{name}.agent.yaml")));

    private static ToolListContract Contract(string server) =>
        ToolListContract.Parse(File.ReadAllText(Path.Combine(RepositoryRoot.Path, "contracts", "agent-mcp", $"{server}.tools.json")));

    private static string Source(string relativePath) => File.ReadAllText(Path.Combine(RepositoryRoot.Path, relativePath));

    [Fact]
    public void TheNotificationAgent_HasExactlyTwoTools_BothReads_AndNoOthers()
    {
        var definition = Definition("planner-notification");
        var contract = Contract(definition.Tools.Server);

        definition.Name.Should().Be(DraftShipperNotificationHandler.AgentName);
        definition.Tools.Names.Should().BeEquivalentTo("get_shipper_notification_template", "get_shipper_view");
        contract.Tools.Select(tool => tool.Name).Should().BeEquivalentTo("get_shipper_notification_template", "get_shipper_view");
        contract.Tools.Should().OnlyContain(tool => tool.IsRead, "an Agent has no write tool (ADR 0040)");
        contract.Unknown(definition).Should().BeEmpty();
        contract.Role.Should().Be(AgentRoles.PlannerNotificationRead);
        definition.Model.MaxToolCalls.Should().Be(2);
    }

    [Fact]
    public void TheNotificationAgentsRole_IsItsOwn_AndReachesNoOtherAgentViewTool()
    {
        var notification = Contract("planner-notification");

        OtherTmsAgents.Select(Contract).Should().OnlyContain(other => other.Role != notification.Role && other.Server != notification.Server);
        notification.Tools.Select(tool => tool.Name).Should().NotContain(["get_assignment_options", "get_exception_timeline", "get_transport_order", "get_rejected_booking"]);
        Definition("planner").Tools.Names.Should().NotContain(["get_shipper_view", "get_shipper_notification_template"]);
        Definition("planner-exception-note").Tools.Names.Should().NotContain(["get_shipper_view", "get_shipper_notification_template"]);
        Definition("customer-service").Tools.Names.Should().NotContain("get_shipper_notification_template");
    }

    [Fact]
    public void EachTmsAgentView_RequiresOnlyItsOwnRole()
    {
        var notification = Source("src/Tms/Norvia.Tms.Api/Agents/PlannerNotificationAgentEndpoints.cs");

        notification.Should().Contain("RequireRole(AgentRoles.PlannerNotificationRead)").And.NotContain("AgentRoles.PlannerRead").And.NotContain("AgentRoles.CustomerService");
        foreach (var other in new[] { "CustomerServiceAgentEndpoints.cs", "CustomerServiceReplyAgentEndpoints.cs", "PlannerAgentEndpoints.cs" })
        {
            Source($"src/Tms/Norvia.Tms.Api/Agents/{other}").Should().NotContain("PlannerNotificationRead");
        }

        PlannerNotificationAgentEndpoints.Flow.Should().StartWith("agent-view-")
            .And.NotBe(PlannerAgentEndpoints.Flow).And.NotBe(CustomerServiceAgentEndpoints.Flow).And.NotBe(CustomerServiceReplyAgentEndpoints.Flow);
    }

    [Fact]
    public void TheNotificationAgentView_IsOnlyReads_SoNoToolCanWriteOrSend()
    {
        var endpoints = Source("src/Tms/Norvia.Tms.Api/Agents/PlannerNotificationAgentEndpoints.cs");

        endpoints.Should().Contain("MapGet(").And.NotContainAny("MapPost(", "MapPut(", "MapPatch(", "MapDelete(");
        endpoints.Should().NotContain("NotifyShipperCommand", "the notification is sent only by the Planner through P7's flow");
    }

    [Fact]
    public void TheDraftHandler_IsAReadHandler_ThatSendsNothing()
    {
        var source = Source("src/Tms/Norvia.Tms.Application/Agents/Planner/DraftShipperNotificationHandler.cs");

        source.Should().Contain("IReadRepository<").And.NotContain("IRepository<").And.NotContain("SaveChangesAsync").And.NotContain("NotifyShipperCommand");
    }

    [Fact]
    public void OnlyTheNotificationFlow_AcceptsTheNotificationAgent_ForANotifyShipperTrace()
    {
        Source("src/Tms/Norvia.Tms.Api/Exceptions/ExceptionEndpoints.cs").Should().Contain("NotificationDraftingAgents = [DraftShipperNotificationHandler.AgentName]");
    }

    [Fact]
    public void TheNotificationServerInAzure_HasTheTwoToolsOfTheContract_AViewOfItsOwn_AndItsOwnRole()
    {
        var resources = BicepSnapshot.Resources(
            "infra/estate/planner-notification-mcp.bicep",
            ["param environmentName = 'env'", "param apiManagementName = 'apim'", "param apiManagementClientId = '00000000-0000-0000-0000-000000000002'", "param tmsAgentApiAppId = '00000000-0000-0000-0000-000000000003'"]);

        resources.Where(resource => resource["type"]!.GetValue<string>() == "Microsoft.ApiManagement/service/apis/tools")
            .Select(tool => tool["properties"]!["displayName"]!.GetValue<string>()).Should().BeEquivalentTo("get_shipper_notification_template", "get_shipper_view");
        var app = resources.Single(resource => resource["type"]!.GetValue<string>() == "Microsoft.Graph/applications@v1.0")["properties"]!;
        app["displayName"]!.GetValue<string>().Should().Be("Norvia planner-notification agent view (env)", "assign-agent-roles.ps1 looks the view up by this name");
        app["appRoles"]!.AsArray().Select(role => role!["value"]!.GetValue<string>()).Should().Equal(AgentRoles.PlannerNotificationRead);
        Source("infra/estate.bicep").Should().Contain("module plannerNotificationMcp 'estate/planner-notification-mcp.bicep'");
        Source("infra/deploy-agents.ps1").Should().Contain("'planner-notification'").And.Contain("/v1/agent/planner-notification");
    }
}
