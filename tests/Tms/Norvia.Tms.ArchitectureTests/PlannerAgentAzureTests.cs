using System.Text.Json.Nodes;

namespace Norvia.Tms.ArchitectureTests;

/// <summary>
/// The Planner agent in Azure (#441, ADR 0040): its API Management MCP server over the TMS API's Planner view, the one role on its agent view,
/// and the scripts that publish the Agent and smoke-test it. The Bicep is evaluated locally with <c>bicep snapshot</c>; nothing is deployed. The
/// tool-list drift test is in two parts: the Bicep reads the contract file (<see cref="TheMcpServer_HasTheContractsTools_AndNoOthers"/>), and the
/// stand-in serves that file (its unit tests, PlannerServerTests), so the stand-in's list is the Bicep's.
/// </summary>
public sealed class PlannerAgentAzureTests
{
    private const string McpModule = "infra/estate/planner-mcp.bicep";
    private const string Contract = "contracts/agent-mcp/planner.tools.json";
    private const string Role = "TransportOrders.PlannerAgent.Read";
    private const string ApimClient = "00000000-0000-0000-0000-000000000002";
    private const string TmsAgentApi = "00000000-0000-0000-0000-000000000003";

    private static readonly string[] ApplicationOnly = ["Application"];

    private static readonly JsonNode ContractFile = JsonNode.Parse(File.ReadAllText(Path.Combine(RepositoryRoot.Path, Contract)))!;

    private static List<JsonNode> McpResources(params string[] extra) =>
        BicepSnapshot.Resources(
            McpModule,
            [
                "param environmentName = 'env'", "param apiManagementName = 'apim'", $"param apiManagementClientId = '{ApimClient}'",
                $"param tmsAgentApiAppId = '{TmsAgentApi}'", .. extra,
            ]);

    private static IEnumerable<JsonNode> Of(List<JsonNode> resources, string type) => resources.Where(resource => resource["type"]!.GetValue<string>() == type);

    private static string Script(string name) => File.ReadAllText(Path.Combine(RepositoryRoot.Path, "infra", name));

    private static string Text(string path) => File.ReadAllText(Path.Combine(RepositoryRoot.Path, path));

    [Fact]
    public void TheMcpServer_HasTheContractsTools_AndNoOthers()
    {
        var resources = McpResources();
        var tools = Of(resources, "Microsoft.ApiManagement/service/apis/tools").ToList();

        tools.Select(tool => tool["properties"]!["displayName"]!.GetValue<string>())
            .Should().Equal(ContractFile["tools"]!.AsArray().Select(tool => tool!["name"]!.GetValue<string>()));
        tools.Select(tool => tool["properties"]!["description"]!.GetValue<string>())
            .Should().Equal(ContractFile["tools"]!.AsArray().Select(tool => tool!["description"]!.GetValue<string>()));
        Of(resources, "Microsoft.ApiManagement/service/apis/operations").Select(operation => operation["properties"]!["method"]!.GetValue<string>())
            .Should().OnlyContain(method => method == "GET");
        Of(resources, "Microsoft.ApiManagement/service/apis/operations").Select(operation => operation["properties"]!["urlTemplate"]!.GetValue<string>())
            .Should().Equal(ContractFile["tools"]!.AsArray().Select(tool => tool!["path"]!.GetValue<string>()));
        tools.Select(tool => tool["properties"]!["displayName"]!.GetValue<string>()).Should().Equal("get_assignment_options", "get_exception_timeline");
    }

    [Fact]
    public void TheMcpServer_IsAtAgentsPlanner_InNoProduct_AndTakesNoSubscriptionKey()
    {
        var resources = McpResources();
        var apis = Of(resources, "Microsoft.ApiManagement/service/apis").ToList();

        apis.Select(api => api["properties"]!["path"]!.GetValue<string>()).Should().BeEquivalentTo("agent-views/planner", "agents/planner");
        apis.Should().OnlyContain(api => !api["properties"]!["subscriptionRequired"]!.GetValue<bool>(), "the Agent's Entra token is the only credential");
        Of(resources, "Microsoft.ApiManagement/service/products/apis").Should().BeEmpty();
    }

    // The app registrations' client IDs are known only when the template is deployed, so the policy is not in the snapshot. The module's own
    // tests (AgentMcpServerTests) cover the policy with the module's values filled in; this checks the call gives it the right ones.
    [Fact]
    public void TheModuleCall_GivesThePolicyTheContractsTools_TheContractsRole_ThePlannerViewAsAudience_AndTheTmsAgentApiAsBackend_WithARateLimit()
    {
        var bicep = Text(McpModule);

        bicep.Should().Contain($"loadJsonContent('../../{Contract}')");
        bicep.Should().Contain("tools: contract.tools").And.Contain("requiredRole: role").And.Contain("audience: view.appId");
        bicep.Should().Contain("backendId: tmsBackend.name").And.Contain("backendAudience: tmsAgentApiAppId").And.Contain("apiManagementIdentityClientId: apiManagementClientId");
        bicep.Should().MatchRegex(@"rateLimitCalls: \d+", "the Planner agent has a rate limit of its own");
    }

    [Fact]
    public void TheBackend_IsThePlannerView_APlaceholderUntilTheTmsIsDeployed_AndThenItsAddress()
    {
        Of(McpResources(), "Microsoft.ApiManagement/service/backends").Single()["properties"]!["url"]!.GetValue<string>().Should().Be("https://tms.invalid/v1/agent/planner");
        Of(McpResources("param tmsBaseUrl = 'https://tms-api.example.io'"), "Microsoft.ApiManagement/service/backends").Single()["properties"]!["url"]!.GetValue<string>()
            .Should().Be("https://tms-api.example.io/v1/agent/planner");
    }

    [Fact]
    public void TheAgentView_HasOneRole_ThePlannersRead_ForApplicationsOnly_AndEntraIssuesNoTokenWithoutIt()
    {
        var app = Of(McpResources(), "Microsoft.Graph/applications@v1.0").Single()["properties"]!;
        var roles = app["appRoles"]!.AsArray().Select(role => role!).ToList();

        roles.Should().ContainSingle().Which["value"]!.GetValue<string>().Should().Be(Role);
        roles.Single()["allowedMemberTypes"]!.AsArray().Select(type => type!.GetValue<string>()).Should().Equal(ApplicationOnly);
        app["api"]!["requestedAccessTokenVersion"]!.GetValue<int>().Should().Be(2, "azp is the caller's client ID in a v2 token");
        app["displayName"]!.GetValue<string>().Should().Be("Norvia planner agent view (env)", "assign-agent-roles.ps1 and the smoke run look the view up by this name");
        ContractFile["role"]!.GetValue<string>().Should().Be(Role);
        Of(McpResources(), "Microsoft.Graph/servicePrincipals@v1.0").Single()["properties"]!["appRoleAssignmentRequired"]!.GetValue<bool>().Should().BeTrue();
    }

    [Fact]
    public void TheModule_GivesNoRoleToAnyone_TheAgentsIdentityIsGivenItsByTheScript_AndHoldsNoAzureRole()
    {
        Of(McpResources(), "Microsoft.Graph/appRoleAssignedTo@v1.0").Should().BeEmpty("API Management's role on the TMS agent API is given where the API is made, in customer-service-mcp.bicep");
        Text(McpModule).Should().NotContain("roleAssignments", "the Agent holds no Azure role");
        Text("infra/estate/customer-service-mcp.bicep").Should().Contain("apiManagementReadsThePlannerAgentApi");
        Script("assign-agent-roles.ps1").Should().Contain("appRoleAssignedTo");
    }

    [Fact]
    public void TheEstate_CallsTheModule_WithTheTmsAgentApi_AndTheTmsAddress()
    {
        var estate = Text("infra/estate.bicep");

        estate.Should().Contain("module plannerMcp 'estate/planner-mcp.bicep'").And.Contain("tmsAgentApiAppId: customerServiceMcp.outputs.tmsAgentApiAppId").And.Contain("tmsBaseUrl: tmsBaseUrl");
    }

    [Fact]
    public void DeployScript_PointsThePlannerBackendAtTheTmsApi_AndKeepsTheAddress()
    {
        Script("deploy-agents.ps1").Should().Contain("'planner'").And.Contain("tms-planner-agent-api").And.Contain("ESTATE_TMS_BASE_URL").And.Contain("/v1/agent/planner");
        Of(McpResources(), "Microsoft.ApiManagement/service/backends").Single()["name"]!.GetValue<string>().Should().EndWith("tms-planner-agent-api");
    }

    [Fact]
    public void TheAgentsDefinition_NamesTheContractsServer_AndOnlyItsTool_AndTheMcpToolIsReadOnly()
    {
        var definition = Text("agents/planner.agent.yaml");

        definition.Should().Contain($"server: {ContractFile["server"]!.GetValue<string>()}");
        definition.Should().Contain("- get_assignment_options").And.Contain("maxToolCalls: 1");
        definition.Should().Contain("correlationIdHeader: X-Correlation-ID", "the Agent's tool calls join the run's trail");
    }

    [Fact]
    public void SmokeRun_ChecksTheRoles_TheOverCapacityWarning_ACarriersCost_NoAddressOrConsigneeInTheToolCalls_TheHops_AndThatNothingIsKept()
    {
        var script = Script("smoke-test-planner-agent.ps1");

        script.Should().Contain("role assignment list").And.Contain("holds no Azure role");
        script.Should().Contain("agent-mcp-planner").And.Contain("agent-view-planner");
        script.Should().Contain("OverCapacity").And.Contain("compares a Carrier's cost").And.Contain("carrierCost");
        script.Should().Contain("no street address or Consignee field appears in the tool calls").And.Contain("holds $role on the Planner agent view and nothing else");
        script.Should().MatchRegex(@"store\s*=\s*\$false");
        script.Should().MatchRegex(@"max_tool_calls\s*=\s*1");
        script.Should().MatchRegex(@"structured_inputs\s*=\s*@\{\s*correlationId\s*=\s*\$correlation\s*\}");
    }

    [Fact]
    public void SmokeRun_NeverEchoesATokenOrASecret()
    {
        var lines = Script("smoke-test-planner-agent.ps1").Split('\n').Where(line => line.TrimStart().StartsWith("Write-", StringComparison.Ordinal));

        lines.Should().NotContain(line => line.Contains("token", StringComparison.OrdinalIgnoreCase) || line.Contains("SECRET", StringComparison.Ordinal) || line.Contains("$key", StringComparison.Ordinal));
    }
}
