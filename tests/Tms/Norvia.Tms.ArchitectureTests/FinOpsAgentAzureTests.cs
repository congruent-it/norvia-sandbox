using System.Text.Json.Nodes;

namespace Norvia.Tms.ArchitectureTests;

/// <summary>
/// The FinOps agent in Azure (#511, ADR 0040): the API Management MCP server over the cost tools of the Estate health check's agent API, its
/// view with the one role, and no Azure role. The Bicep is evaluated locally with <c>bicep snapshot</c>; nothing is deployed.
/// </summary>
public sealed class FinOpsAgentAzureTests
{
    private const string McpModule = "infra/estate/health-check-cost-mcp.bicep";
    private const string Contract = "contracts/agent-mcp/health-check-cost.tools.json";
    private const string ApimClient = "00000000-0000-0000-0000-000000000002";

    private static readonly JsonNode ContractFile = JsonNode.Parse(File.ReadAllText(Path.Combine(RepositoryRoot.Path, Contract)))!;

    private static List<JsonNode> McpResources() =>
        BicepSnapshot.Resources(McpModule, ["param environmentName = 'env'", "param apiManagementName = 'apim'", $"param apiManagementClientId = '{ApimClient}'"]);

    private static IEnumerable<JsonNode> Of(List<JsonNode> resources, string type) => resources.Where(resource => resource["type"]!.GetValue<string>() == type);

    [Fact]
    public void TheMcpServer_HasTheContractsTools_AndNoOthers_AtAgentsHealthCheckCost()
    {
        var resources = McpResources();
        var tools = Of(resources, "Microsoft.ApiManagement/service/apis/tools").ToList();

        tools.Select(tool => tool["properties"]!["displayName"]!.GetValue<string>())
            .Should().Equal(ContractFile["tools"]!.AsArray().Select(tool => tool!["name"]!.GetValue<string>()))
            .And.Equal("get_cost_view", "get_rising_spend_findings");
        tools.Select(tool => tool["properties"]!["description"]!.GetValue<string>()).Should().Equal(ContractFile["tools"]!.AsArray().Select(tool => tool!["description"]!.GetValue<string>()));
        Of(resources, "Microsoft.ApiManagement/service/apis/operations").Select(operation => operation["properties"]!["method"]!.GetValue<string>()).Should().OnlyContain(method => method == "GET");
        Of(resources, "Microsoft.ApiManagement/service/apis/operations").Select(operation => operation["properties"]!["urlTemplate"]!.GetValue<string>()).Should().Equal("/cost", "/rising-spend");
        var mcp = Of(resources, "Microsoft.ApiManagement/service/apis").Single(api => api["properties"]!["type"]?.GetValue<string>() == "mcp");
        mcp["properties"]!["path"]!.GetValue<string>().Should().Be("agents/health-check-cost");
        mcp["properties"]!["subscriptionRequired"]!.GetValue<bool>().Should().BeFalse();
    }

    [Fact]
    public void TheView_HasOnlyTheCostCallRole_ForApplicationsOnly_AndEntraIssuesNoTokenWithoutIt()
    {
        var resources = McpResources();
        var app = Of(resources, "Microsoft.Graph/applications@v1.0").Single()["properties"]!;

        app["displayName"]!.GetValue<string>().Should().Be("Norvia health-check-cost agent view (env)", "assign-agent-roles.ps1 looks the view up by this name");
        var role = app["appRoles"]!.AsArray().Should().ContainSingle().Subject!;
        role["value"]!.GetValue<string>().Should().Be("HealthCheck.Cost.Read").And.Be(ContractFile["role"]!.GetValue<string>());
        role["allowedMemberTypes"]!.AsArray().Select(type => type!.GetValue<string>()).Should().Equal("Application");
        Of(resources, "Microsoft.Graph/servicePrincipals@v1.0").Single()["properties"]!["appRoleAssignmentRequired"]!.GetValue<bool>().Should().BeTrue();
        Of(resources, "Microsoft.Graph/appRoleAssignedTo@v1.0").Should().BeEmpty("the Agent's identity is given its role by assign-agent-roles.ps1 after every publish");
    }

    [Fact]
    public void TheModuleCall_UsesTheAppsAgentApiBackend_AndGivesTheAgentNoAzureRole()
    {
        var bicep = File.ReadAllText(Path.Combine(RepositoryRoot.Path, McpModule));

        bicep.Should().Contain($"loadJsonContent('../../{Contract}')");
        bicep.Should().Contain("tools: contract.tools").And.Contain("requiredRole: role").And.Contain("audience: view.appId");
        bicep.Should().Contain("backendId: healthCheckBackend.name").And.Contain("backendAudience: healthCheckAgentApi.appId").And.Contain("apiManagementIdentityClientId: apiManagementClientId");
        bicep.Should().MatchRegex(@"rateLimitCalls: \d+");
        bicep.Should().NotContain("roleAssignments", "the FinOps agent holds no Azure role: reading Cost Management is the app's own identity's job");
    }

    [Fact]
    public void TheAgentsDefinition_NamesTheContractsServer_AndOnlyItsTools()
    {
        var definition = File.ReadAllText(Path.Combine(RepositoryRoot.Path, "agents/finops.agent.yaml"));

        definition.Should().Contain($"server: {ContractFile["server"]!.GetValue<string>()}");
        foreach (var tool in ContractFile["tools"]!.AsArray().Select(tool => tool!["name"]!.GetValue<string>()))
        {
            definition.Should().Contain($"- {tool}");
        }
    }

    [Fact]
    public void DeployScript_PointsTheCostServersBackendAtTheApp()
    {
        File.ReadAllText(Path.Combine(RepositoryRoot.Path, "infra", "deploy-agents.ps1")).Should().Contain("'health-check-cost' { Set-BackendAddress $server health-check-web health-check-agent-api ESTATE_HEALTH_CHECK_BASE_URL }");
    }
}
