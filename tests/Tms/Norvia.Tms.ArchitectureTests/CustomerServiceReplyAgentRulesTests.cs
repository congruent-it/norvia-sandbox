using NetArchTest.Rules;
using Norvia.Agents.Definitions;
using Norvia.Agents.Tools;
using Norvia.Tms.Api.Agents;
using Norvia.Tms.Application.Agents;
using Norvia.Tms.Application.Agents.Reply;

namespace Norvia.Tms.ArchitectureTests;

/// <summary>
/// The Customer service reply agent (#426, ADR 0011, ADR 0040): the boundary is a tool, not an instruction. It is a separate definition and
/// identity with exactly one tool, the Shipper view, behind a role of its own, so it cannot reach the Customer service agent's tools and
/// so cannot name the Carrier or an internal Exception.
/// </summary>
public sealed class CustomerServiceReplyAgentRulesTests
{
    private static AgentDefinition Definition(string name) =>
        AgentDefinitionLoader.Parse(File.ReadAllText(Path.Combine(RepositoryRoot.Path, "agents", $"{name}.agent.yaml")));

    private static ToolListContract Contract(string server) =>
        ToolListContract.Parse(File.ReadAllText(Path.Combine(RepositoryRoot.Path, "contracts", "agent-mcp", $"{server}.tools.json")));

    [Fact]
    public void TheReplyAgent_HasExactlyOneTool_AReadOfTheShipperView()
    {
        var definition = Definition("customer-service-reply");
        var contract = Contract(definition.Tools.Server);

        definition.Tools.Names.Should().Equal("get_shipper_view");
        contract.Tools.Should().ContainSingle().Which.Path.Should().EndWith("/shipper-view");
        contract.Tools.Should().OnlyContain(tool => tool.IsRead, "an Agent has no write tool (ADR 0040)");
        contract.Unknown(definition).Should().BeEmpty();
        definition.Model.MaxToolCalls.Should().Be(1);
    }

    [Fact]
    public void TheReplyAgentsRole_CannotReachTheOtherAgentViewTools()
    {
        var reply = Contract("customer-service-reply");
        var customerService = Contract("customer-service");

        reply.Role.Should().Be(AgentRoles.CustomerServiceReplyRead).And.NotBe(customerService.Role);
        customerService.Role.Should().Be(AgentRoles.CustomerServiceRead);
        reply.Server.Should().NotBe(customerService.Server, "each Agent has an MCP server and an audience of its own");
        reply.Tools.Select(tool => tool.Name).Should().NotIntersectWith(customerService.Tools.Select(tool => tool.Name));
        Definition("customer-service").Tools.Names.Should().NotContain("get_shipper_view", "the Customer service agent does not read the Shipper view");
    }

    [Fact]
    public void EachAgentView_RequiresOnlyItsOwnRole()
    {
        var customerService = File.ReadAllText(Path.Combine(RepositoryRoot.Path, "src/Tms/Norvia.Tms.Api/Agents/CustomerServiceAgentEndpoints.cs"));
        var reply = File.ReadAllText(Path.Combine(RepositoryRoot.Path, "src/Tms/Norvia.Tms.Api/Agents/CustomerServiceReplyAgentEndpoints.cs"));

        customerService.Should().Contain("RequireRole(AgentRoles.CustomerServiceRead)").And.NotContain("CustomerServiceReplyRead");
        reply.Should().Contain("RequireRole(AgentRoles.CustomerServiceReplyRead)").And.NotContain("AgentRoles.CustomerServiceRead");
        CustomerServiceReplyAgentEndpoints.Flow.Should().StartWith("agent-view-").And.NotBe(CustomerServiceAgentEndpoints.Flow);
    }

    [Fact]
    public void TheReplyAgentsView_IsBuiltOnTheShipperTrackingReadModel_AndKnowsNoCarrierShipmentOrException()
    {
        var application = Types.InAssembly(typeof(GetShipperViewHandler).Assembly)
            .That().ResideInNamespace("Norvia.Tms.Application.Agents.Reply")
            .ShouldNot().HaveDependencyOnAny("Norvia.Tms.Domain.Exceptions", "Norvia.Tms.Domain.Carriers", "Norvia.Tms.Domain.CarrierBookings", "Norvia.Tms.Application.Carriers")
            .GetResult();

        application.IsSuccessful.Should().BeTrue(string.Join(", ", application.FailingTypeNames ?? []));
    }

    [Fact]
    public void TheReplyServerInAzure_HasTheOneToolOfTheContract_AViewOfItsOwn_AndItsOwnRole()
    {
        var resources = BicepSnapshot.Resources(
            "infra/estate/customer-service-reply-mcp.bicep",
            ["param environmentName = 'env'", "param apiManagementName = 'apim'", "param apiManagementClientId = '00000000-0000-0000-0000-000000000002'", "param tmsAgentApiAppId = '00000000-0000-0000-0000-000000000003'"]);

        resources.Where(resource => resource["type"]!.GetValue<string>() == "Microsoft.ApiManagement/service/apis/tools")
            .Select(tool => tool["properties"]!["displayName"]!.GetValue<string>()).Should().Equal("get_shipper_view");
        var app = resources.Single(resource => resource["type"]!.GetValue<string>() == "Microsoft.Graph/applications@v1.0")["properties"]!;
        app["displayName"]!.GetValue<string>().Should().Be("Norvia customer-service-reply agent view (env)", "assign-agent-roles.ps1 looks the view up by this name");
        app["appRoles"]!.AsArray().Select(role => role!["value"]!.GetValue<string>()).Should().Equal(AgentRoles.CustomerServiceReplyRead);
        File.ReadAllText(Path.Combine(RepositoryRoot.Path, "infra/estate.bicep")).Should().Contain("module customerServiceReplyMcp 'estate/customer-service-reply-mcp.bicep'");
        File.ReadAllText(Path.Combine(RepositoryRoot.Path, "infra/deploy-agents.ps1")).Should().Contain("'customer-service-reply'").And.Contain("/v1/agent/customer-service-reply");
    }
}
