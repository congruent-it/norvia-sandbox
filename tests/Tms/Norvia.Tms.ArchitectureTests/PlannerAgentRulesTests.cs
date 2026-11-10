using NetArchTest.Rules;
using Norvia.Agents.Definitions;
using Norvia.Agents.Tools;
using Norvia.Tms.Api.Agents;
using Norvia.Tms.Application.Agents;
using Norvia.Tms.Application.Agents.Planner;

namespace Norvia.Tms.ArchitectureTests;

/// <summary>
/// The Planner agent (#440, ADR 0040): the boundary is a tool, not an instruction. It is a separate definition and identity with exactly one
/// read tool, the assignment options, behind a role of its own, so it can reach no other Agent's tools and no tool can write.
/// </summary>
public sealed class PlannerAgentRulesTests
{
    private static readonly string[] OtherTmsAgents = ["customer-service", "customer-service-reply"];

    private static AgentDefinition Definition(string name) =>
        AgentDefinitionLoader.Parse(File.ReadAllText(Path.Combine(RepositoryRoot.Path, "agents", $"{name}.agent.yaml")));

    private static ToolListContract Contract(string server) =>
        ToolListContract.Parse(File.ReadAllText(Path.Combine(RepositoryRoot.Path, "contracts", "agent-mcp", $"{server}.tools.json")));

    [Fact]
    public void ThePlannerAgent_HasExactlyOneTool_AReadOfTheAssignmentOptions()
    {
        var definition = Definition("planner");
        var contract = Contract(definition.Tools.Server);

        definition.Tools.Names.Should().Equal("get_assignment_options");
        contract.Tools.Should().ContainSingle().Which.Path.Should().EndWith("/assignment-options");
        contract.Tools.Should().OnlyContain(tool => tool.IsRead, "an Agent has no write tool (ADR 0040)");
        contract.Unknown(definition).Should().BeEmpty();
        definition.Model.MaxToolCalls.Should().Be(1);
    }

    [Fact]
    public void ThePlannerAgentsRole_IsItsOwn_AndHoldsNoOtherAgentsTools()
    {
        var planner = Contract("planner");

        planner.Role.Should().Be(AgentRoles.PlannerRead);
        OtherTmsAgents.Select(Contract).Should().OnlyContain(other => other.Role != planner.Role && other.Server != planner.Server);
        OtherTmsAgents.SelectMany(server => Contract(server).Tools).Select(tool => tool.Name).Should().NotContain("get_assignment_options");
        Definition("customer-service").Tools.Names.Should().NotContain("get_assignment_options");
        Definition("customer-service-reply").Tools.Names.Should().NotContain("get_assignment_options");
    }

    [Fact]
    public void EachTmsAgentView_RequiresOnlyItsOwnRole()
    {
        var planner = File.ReadAllText(Path.Combine(RepositoryRoot.Path, "src/Tms/Norvia.Tms.Api/Agents/PlannerAgentEndpoints.cs"));

        planner.Should().Contain("RequireRole(AgentRoles.PlannerRead)").And.NotContain("AgentRoles.CustomerService");
        foreach (var other in new[] { "CustomerServiceAgentEndpoints.cs", "CustomerServiceReplyAgentEndpoints.cs" })
        {
            File.ReadAllText(Path.Combine(RepositoryRoot.Path, "src/Tms/Norvia.Tms.Api/Agents", other)).Should().NotContain("PlannerRead");
        }

        PlannerAgentEndpoints.Flow.Should().StartWith("agent-view-").And.NotBe(CustomerServiceAgentEndpoints.Flow).And.NotBe(CustomerServiceReplyAgentEndpoints.Flow);
    }

    [Fact]
    public void ThePlannerAgentView_IsOnlyReads_SoNoToolCanWrite()
    {
        var endpoints = File.ReadAllText(Path.Combine(RepositoryRoot.Path, "src/Tms/Norvia.Tms.Api/Agents/PlannerAgentEndpoints.cs"));

        endpoints.Should().Contain("MapGet(").And.NotContainAny("MapPost(", "MapPut(", "MapPatch(", "MapDelete(");
    }

    [Fact]
    public void TheOptionsQuery_IsAReadHandler_ThatSendsNoCommandAndWritesNothing()
    {
        var application = Types.InAssembly(typeof(GetAssignmentOptionsHandler).Assembly)
            .That().ResideInNamespace("Norvia.Tms.Application.Agents.Planner")
            .ShouldNot().HaveDependencyOnAny("Norvia.Tms.Application.TransportOrders.Assign", "Norvia.Tms.Application.Shipments.Reassign", "Norvia.Tms.Application.Shipments.Release")
            .GetResult();
        var source = File.ReadAllText(Path.Combine(RepositoryRoot.Path, "src/Tms/Norvia.Tms.Application/Agents/Planner/GetAssignmentOptionsHandler.cs"));

        application.IsSuccessful.Should().BeTrue(string.Join(", ", application.FailingTypeNames ?? []));
        source.Should().Contain("IReadRepository<").And.NotContain("IRepository<").And.NotContain("SaveChangesAsync");
    }

    [Fact]
    public void ThePlannerServerInAzure_HasTheOneToolOfTheContract_AViewOfItsOwn_AndItsOwnRole()
    {
        var resources = BicepSnapshot.Resources(
            "infra/estate/planner-mcp.bicep",
            ["param environmentName = 'env'", "param apiManagementName = 'apim'", "param apiManagementClientId = '00000000-0000-0000-0000-000000000002'", "param tmsAgentApiAppId = '00000000-0000-0000-0000-000000000003'"]);

        resources.Where(resource => resource["type"]!.GetValue<string>() == "Microsoft.ApiManagement/service/apis/tools")
            .Select(tool => tool["properties"]!["displayName"]!.GetValue<string>()).Should().Equal("get_assignment_options");
        var app = resources.Single(resource => resource["type"]!.GetValue<string>() == "Microsoft.Graph/applications@v1.0")["properties"]!;
        app["displayName"]!.GetValue<string>().Should().Be("Norvia planner agent view (env)", "assign-agent-roles.ps1 looks the view up by this name");
        app["appRoles"]!.AsArray().Select(role => role!["value"]!.GetValue<string>()).Should().Equal(AgentRoles.PlannerRead);
        File.ReadAllText(Path.Combine(RepositoryRoot.Path, "infra/estate.bicep")).Should().Contain("module plannerMcp 'estate/planner-mcp.bicep'");
        File.ReadAllText(Path.Combine(RepositoryRoot.Path, "infra/deploy-agents.ps1")).Should().Contain("'planner'").And.Contain("/v1/agent/planner");
    }
}
