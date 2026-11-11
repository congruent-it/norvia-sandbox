using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Norvia.Tms.ArchitectureTests;

/// <summary>
/// The Customer service agent in Azure (#422, ADR 0040): the API Management MCP server over the TMS API's agent view, the one role on each of its
/// two app registrations, and the scripts that publish the Agent and smoke-test it. The Bicep is evaluated locally with <c>bicep snapshot</c>;
/// nothing is deployed. The tool-list drift test is in two parts: the Bicep reads the contract file
/// (<see cref="TheMcpServer_HasTheContractsTools_AndNoOthers"/>), and the stand-in serves that file (its unit tests), so the stand-in's list is the Bicep's.
/// </summary>
public sealed class CustomerServiceAgentAzureTests
{
    private const string McpModule = "infra/estate/customer-service-mcp.bicep";
    private const string Contract = "contracts/agent-mcp/customer-service.tools.json";
    private const string Role = "TransportOrders.CustomerServiceAgent.Read";
    private const string ReplyRole = "TransportOrders.CustomerServiceReplyAgent.Read";
    private const string PlannerRole = "TransportOrders.PlannerAgent.Read";
    private const string PlannerNotificationRole = "TransportOrders.PlannerNotificationAgent.Read";
    private const string ApimPrincipal = "00000000-0000-0000-0000-000000000001";
    private const string ApimClient = "00000000-0000-0000-0000-000000000002";

    private static readonly string[] ApplicationOnly = ["Application"];

    private static readonly JsonNode ContractFile = JsonNode.Parse(File.ReadAllText(Path.Combine(RepositoryRoot.Path, Contract)))!;

    private static List<JsonNode> McpResources(params string[] extra) =>
        BicepSnapshot.Resources(
            McpModule,
            [
                "param environmentName = 'env'", "param apiManagementName = 'apim'", $"param apiManagementPrincipalId = '{ApimPrincipal}'",
                $"param apiManagementClientId = '{ApimClient}'", .. extra,
            ]);

    private static IEnumerable<JsonNode> Of(List<JsonNode> resources, string type) => resources.Where(resource => resource["type"]!.GetValue<string>() == type);

    private static string Script(string name) => File.ReadAllText(Path.Combine(RepositoryRoot.Path, "infra", name));

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
        tools.Should().HaveCount(6, "get_rejected_booking, the four tools that summarise a Transport Order, then get_release_eligibility");
    }

    [Fact]
    public void TheApiInFrontOfTheTmsAgentView_IsOnlyAnAgentView_InNoProduct_AndTheMcpServerIsAtAgentsCustomerService()
    {
        var apis = Of(McpResources(), "Microsoft.ApiManagement/service/apis").ToList();

        apis.Select(api => api["properties"]!["path"]!.GetValue<string>()).Should().BeEquivalentTo("agent-views/customer-service", "agents/customer-service");
        apis.Should().OnlyContain(api => !api["properties"]!["subscriptionRequired"]!.GetValue<bool>(), "the Agent's Entra token is the only credential");
        Of(McpResources(), "Microsoft.ApiManagement/service/products/apis").Should().BeEmpty();
    }

    // The app registrations' client IDs are known only when the template is deployed, so the policy is not in the snapshot. The module's own
    // tests (AgentMcpServerTests) cover the policy with the module's values filled in; this checks the call gives it the right ones.
    [Fact]
    public void TheModuleCall_GivesThePolicyTheContractsTools_TheContractsRole_TheAgentViewAsAudience_AndTheTmsAgentApiAsBackend_WithARateLimit()
    {
        var bicep = File.ReadAllText(Path.Combine(RepositoryRoot.Path, McpModule));

        bicep.Should().Contain($"loadJsonContent('../../{Contract}')");
        bicep.Should().Contain("tools: contract.tools").And.Contain("requiredRole: role").And.Contain("audience: view.appId");
        bicep.Should().Contain("backendId: tmsBackend.name").And.Contain("backendAudience: tmsAgentApi.appId").And.Contain("apiManagementIdentityClientId: apiManagementClientId");
        bicep.Should().MatchRegex(@"rateLimitCalls: \d+", "the Agent has a rate limit of its own");
    }

    [Fact]
    public void TheBackend_IsTheTmsAgentView_APlaceholderUntilTheTmsIsDeployed_AndThenItsAddress()
    {
        Of(McpResources(), "Microsoft.ApiManagement/service/backends").Single()["properties"]!["url"]!.GetValue<string>().Should().Be("https://tms.invalid/v1/agent/customer-service");
        Of(McpResources("param tmsBaseUrl = 'https://tms-api.example.io'"), "Microsoft.ApiManagement/service/backends").Single()["properties"]!["url"]!.GetValue<string>()
            .Should().Be("https://tms-api.example.io/v1/agent/customer-service");
    }

    [Fact]
    public void TheViewHasOneRole_TheContractsRead_AndTheTmsAgentApiHasThatAndTheOtherTmsAgentsRoles_ForApplicationsOnly()
    {
        var apps = Of(McpResources(), "Microsoft.Graph/applications@v1.0").Select(app => app["properties"]!).ToList();

        apps.Should().HaveCount(2);
        foreach (var app in apps)
        {
            var roles = app["appRoles"]!.AsArray().Select(role => role!).ToList();
            var isTmsApi = app["displayName"]!.GetValue<string>() == "Norvia TMS agent API (env)";
            roles.Select(role => role["value"]!.GetValue<string>()).Should().Equal(isTmsApi ? [Role, ReplyRole, PlannerRole, PlannerNotificationRole] : [Role]);
            roles.Should().OnlyContain(role => role["allowedMemberTypes"]!.AsArray().Select(type => type!.GetValue<string>()).SequenceEqual(ApplicationOnly));
            app["api"]!["requestedAccessTokenVersion"]!.GetValue<int>().Should().Be(2, "azp is the caller's client ID in a v2 token");
        }

        var names = apps.Select(app => app["displayName"]!.GetValue<string>()).ToList();
        names.Should().Contain("Norvia customer-service agent view (env)", "the scripts look the view up by this name");
        names.Should().Contain("Norvia TMS agent API (env)", "provision-estate.ps1 and the smoke run look the API up by this name");
        ContractFile["role"]!.GetValue<string>().Should().Be(Role);
    }

    [Fact]
    public void EntraIssuesNoTokenForEitherApi_ToAnIdentityWithoutTheRole()
    {
        var principals = Of(McpResources(), "Microsoft.Graph/servicePrincipals@v1.0").ToList();

        principals.Should().HaveCount(2).And.OnlyContain(principal => principal["properties"]!["appRoleAssignmentRequired"]!.GetValue<bool>());
    }

    [Fact]
    public void OnlyApiManagementsIdentity_IsGivenARole_InTheBicep_AndTheAgentsIdentityIsGivenItsByTheScript()
    {
        var assignments = Of(McpResources(), "Microsoft.Graph/appRoleAssignedTo@v1.0").ToList();

        assignments.Should().HaveCount(4, "the Customer service agent's role, the reply agent's, the Planner agent's and the Planner notification agent's, all on the TMS agent API")
            .And.OnlyContain(assignment => assignment["properties"]!["principalId"]!.GetValue<string>() == ApimPrincipal);
        Script("assign-agent-roles.ps1").Should().Contain("appRoleAssignedTo");
        File.ReadAllText(Path.Combine(RepositoryRoot.Path, McpModule)).Should().NotContain("roleAssignments", "the Agent holds no Azure role");
    }

    [Fact]
    public void TheEstate_CallsTheModule_AfterTheOthers_GivesTheTmsUiFoundryUser_AndStaysWithinItsOutputs()
    {
        var estate = File.ReadAllText(Path.Combine(RepositoryRoot.Path, "infra/estate.bicep"));

        estate.Should().Contain("module customerServiceMcp 'estate/customer-service-mcp.bicep'").And.Contain("tmsBaseUrl: tmsBaseUrl");
        estate.Should().Contain("tmsUiIdentity.properties.principalId", "the TMS UI asks its Agent as its own identity");
        Regex.Count(estate, @"(?m)^output ").Should().BeLessThanOrEqualTo(64, "a template may have 64 outputs");
    }

    [Fact]
    public void TheAppHost_RunsTheTmsUiAsItsOwnIdentity_AskingFoundry_AndTheTmsApiTakesTheAgentApisTokenOnItsAgentView()
    {
        var host = File.ReadAllText(Path.Combine(RepositoryRoot.Path, "src/Norvia.AppHost/AppHost.cs"));
        var tmsApi = host[host.IndexOf("tmsApi.WithExternalHttpEndpoints()", StringComparison.Ordinal)..];
        tmsApi = tmsApi[..tmsApi.IndexOf(";\n", StringComparison.Ordinal)];
        var tmsUi = host[host.IndexOf("tmsWeb.WithAzureUserAssignedIdentity(tmsUiIdentity)", StringComparison.Ordinal)..];
        tmsUi = tmsUi[..tmsUi.IndexOf(";\n", StringComparison.Ordinal)];

        tmsApi.Should().Contain("Agents__Api__Audience\", estate.TmsAgentApiClientId").And.Contain("Agents__Api__GatewayClientId");
        tmsUi.Should().Contain("Agents__Enabled").And.Contain("Agents__Foundry__ProjectEndpoint");
    }

    [Fact]
    public void DeployScript_PointsTheTmsBackendAtTheTmsApi_AndKeepsTheAddress()
    {
        Script("deploy-agents.ps1").Should().Contain("'customer-service'").And.Contain("tms-agent-api").And.Contain("ESTATE_TMS_BASE_URL").And.Contain("/v1/agent/customer-service");
        Script("provision-estate.ps1").Should().Contain("Norvia TMS agent API (").And.Contain("estate_tms_agent_api_client_id");
    }

    [Fact]
    public void SmokeRun_ChecksTheRoles_TheRejectionNamed_NoConsigneeInTheToolCalls_TheHopsOnTheTrail_AndThatNothingIsKept()
    {
        var script = Script("smoke-test-customer-service-agent.ps1");

        script.Should().Contain("WeightInvalid").And.Contain("get_rejected_booking").And.Contain("agent-mcp-customer-service").And.Contain("agent-view-customer-service");
        script.Should().Contain("no Consignee field appears in the tool calls").And.Contain("is the only role on the TMS agent API");
        script.Should().MatchRegex(@"store\s*=\s*\$false");
        script.Should().MatchRegex(@"structured_inputs\s*=\s*@\{\s*correlationId\s*=\s*\$correlation\s*\}");
    }

    [Fact]
    public void SmokeRun_NeverEchoesATokenOrASecret()
    {
        var lines = Script("smoke-test-customer-service-agent.ps1").Split('\n').Where(line => line.TrimStart().StartsWith("Write-", StringComparison.Ordinal));

        lines.Should().NotContain(line => line.Contains("token", StringComparison.OrdinalIgnoreCase) || line.Contains("SECRET", StringComparison.Ordinal) || line.Contains("$key", StringComparison.Ordinal));
    }

    [Fact]
    public void TheAgentsDefinition_NamesTheContractsServer_AndOnlyItsTools()
    {
        var definition = File.ReadAllText(Path.Combine(RepositoryRoot.Path, "agents/customer-service.agent.yaml"));
        var tools = ContractFile["tools"]!.AsArray().Select(tool => tool!["name"]!.GetValue<string>()).ToList();

        definition.Should().Contain($"server: {ContractFile["server"]!.GetValue<string>()}");
        foreach (var tool in tools)
        {
            definition.Should().Contain($"- {tool}");
        }
    }
}
