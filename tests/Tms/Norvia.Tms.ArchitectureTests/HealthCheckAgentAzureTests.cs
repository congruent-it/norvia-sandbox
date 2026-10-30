using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Norvia.Tms.ArchitectureTests;

/// <summary>
/// The Estate health-check agent and the app in Azure (#392, ADR 0040): the API Management MCP server over the app's agent API, the one role on
/// each of its two app registrations, the app's identity with Reader (never the Agent's), and the scripts that publish the Agent and smoke-test
/// it. The Bicep is evaluated locally with <c>bicep snapshot</c>; nothing is deployed. The tool-list drift test is in two parts: the Bicep reads
/// the contract file (<see cref="TheMcpServer_HasTheContractsTools_AndNoOthers"/>), and the stand-in serves that file (its unit tests), so the
/// stand-in's list is the Bicep's.
/// </summary>
public sealed class HealthCheckAgentAzureTests
{
    private const string McpModule = "infra/estate/health-check-mcp.bicep";
    private const string AppModule = "infra/estate/health-check.bicep";
    private const string Contract = "contracts/agent-mcp/health-check.tools.json";
    private const string ApimPrincipal = "00000000-0000-0000-0000-000000000001";
    private const string ApimClient = "00000000-0000-0000-0000-000000000002";
    private const string Presenter = "00000000-0000-0000-0000-0000000000aa";
    private const string Reader = "acdd72a7-3385-48ef-bd42-f606fba81ae7";

    private static readonly JsonNode ContractFile = JsonNode.Parse(File.ReadAllText(Path.Combine(RepositoryRoot.Path, Contract)))!;

    private static List<JsonNode> McpResources(params string[] extra) =>
        BicepSnapshot.Resources(
            McpModule,
            [
                "param environmentName = 'env'", "param apiManagementName = 'apim'", $"param apiManagementPrincipalId = '{ApimPrincipal}'",
                $"param apiManagementClientId = '{ApimClient}'", .. extra,
            ]);

    private static List<JsonNode> AppResources(params string[] extra) =>
        BicepSnapshot.Resources(
            AppModule,
            ["param environmentName = 'env'", "param location = 'westeurope'", "param tags = {}", $"param presenterPrincipalId = '{Presenter}'", .. extra]);

    private static IEnumerable<JsonNode> Of(List<JsonNode> resources, string type) => resources.Where(resource => resource["type"]!.GetValue<string>() == type);

    private static string Script(string name) => File.ReadAllText(Path.Combine(RepositoryRoot.Path, "infra", name));

    [Fact]
    public void TheMcpServer_HasTheContractsTools_AndNoOthers()
    {
        var resources = McpResources();
        var tools = Of(resources, "Microsoft.ApiManagement/service/apis/tools").ToList();

        // The name the Agent sees is the display name; the resource's name carries the server's, as a tool's name is unique across the service.
        tools.Select(tool => tool["properties"]!["displayName"]!.GetValue<string>())
            .Should().Equal(ContractFile["tools"]!.AsArray().Select(tool => tool!["name"]!.GetValue<string>()));
        tools.Select(tool => tool["properties"]!["description"]!.GetValue<string>())
            .Should().Equal(ContractFile["tools"]!.AsArray().Select(tool => tool!["description"]!.GetValue<string>()));
        Of(resources, "Microsoft.ApiManagement/service/apis/operations").Select(operation => operation["properties"]!["method"]!.GetValue<string>())
            .Should().OnlyContain(method => method == "GET");
        Of(resources, "Microsoft.ApiManagement/service/apis/operations").Select(operation => operation["properties"]!["urlTemplate"]!.GetValue<string>())
            .Should().Equal(ContractFile["tools"]!.AsArray().Select(tool => tool!["path"]!.GetValue<string>()));
        tools.Should().HaveCount(3, "the health-check agent has run_checks, list_failed_runs and list_dead_letter_queues");
    }

    [Fact]
    public void TheMcpServer_IsAtAgentsHealthCheck_WithTheEntraTokenAsItsOnlyCredential()
    {
        var mcp = Of(McpResources(), "Microsoft.ApiManagement/service/apis").Single(api => api["properties"]!["type"]?.GetValue<string>() == "mcp");

        mcp["properties"]!["path"]!.GetValue<string>().Should().Be("agents/health-check");
        mcp["properties"]!["subscriptionRequired"]!.GetValue<bool>().Should().BeFalse();
    }

    // The app registrations' client IDs are known only when the template is deployed, so the policy is not in the snapshot. The module's own
    // tests (AgentMcpServerTests) cover the policy with the module's values filled in; this checks the call gives it the right ones.
    [Fact]
    public void TheModuleCall_GivesThePolicyTheContractsTools_TheContractsRole_TheAgentViewAsAudience_AndTheAppsAgentApiAsBackend_WithARateLimit()
    {
        var bicep = File.ReadAllText(Path.Combine(RepositoryRoot.Path, McpModule));

        bicep.Should().Contain($"loadJsonContent('../../{Contract}')");
        bicep.Should().Contain("tools: contract.tools").And.Contain("requiredRole: role").And.Contain("audience: view.appId");
        bicep.Should().Contain("backendId: healthCheckBackend.name").And.Contain("backendAudience: healthCheckAgentApi.appId").And.Contain("apiManagementIdentityClientId: apiManagementClientId");
        bicep.Should().MatchRegex(@"rateLimitCalls: \d+", "the Agent has a rate limit of its own");
    }

    [Fact]
    public void TheBackend_IsTheAppsAgentApi_APlaceholderUntilTheAppIsDeployed_AndThenItsAddress()
    {
        Of(McpResources(), "Microsoft.ApiManagement/service/backends").Single()["properties"]!["url"]!.GetValue<string>().Should().Be("https://health-check.invalid/agent/v1");
        Of(McpResources("param healthCheckBaseUrl = 'https://health-check-web.example.io'"), "Microsoft.ApiManagement/service/backends").Single()["properties"]!["url"]!.GetValue<string>()
            .Should().Be("https://health-check-web.example.io/agent/v1");
    }

    [Fact]
    public void BothAgentAppRegistrations_HaveOneRole_TheContractsRead_ForApplicationsOnly()
    {
        var apps = Of(McpResources(), "Microsoft.Graph/applications@v1.0").Select(app => app["properties"]!).ToList();

        apps.Should().HaveCount(2);
        foreach (var app in apps)
        {
            var roles = app["appRoles"]!.AsArray().Select(role => role!).ToList();
            roles.Should().ContainSingle();
            roles[0]["value"]!.GetValue<string>().Should().Be("HealthCheck.Agent.Read");
            roles[0]["allowedMemberTypes"]!.AsArray().Select(type => type!.GetValue<string>()).Should().Equal("Application");
            app["api"]!["requestedAccessTokenVersion"]!.GetValue<int>().Should().Be(2, "azp is the caller's client ID in a v2 token");
        }

        apps.Select(app => app["displayName"]!.GetValue<string>()).Should().Contain("Norvia health-check agent view (env)", "the scripts look the view up by this name");
        apps.Select(app => app["displayName"]!.GetValue<string>()).Should().Contain("Norvia Estate health check agent API (env)", "provision-estate.sh and the smoke run look the API up by this name");
        ContractFile["role"]!.GetValue<string>().Should().Be("HealthCheck.Agent.Read");
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

        assignments.Should().ContainSingle().Which["properties"]!["principalId"]!.GetValue<string>().Should().Be(ApimPrincipal);
        Script("assign-agent-roles.sh").Should().Contain("appRoleAssignedTo");
    }

    [Fact]
    public void TheAppsIdentity_HoldsReaderOnTheEstatesResourceGroupAndNothingElse_AndTheAgentHoldsNoAzureRole()
    {
        var resources = AppResources();
        var assignments = Of(resources, "Microsoft.Authorization/roleAssignments").Select(assignment => assignment["properties"]!).ToList();

        assignments.Should().ContainSingle("Reader is the app's only Azure role").Which["roleDefinitionId"]!.GetValue<string>().Should().EndWith(Reader);
        assignments[0]["principalType"]!.GetValue<string>().Should().Be("ServicePrincipal");
        assignments[0].AsObject().ContainsKey("scope").Should().BeFalse("the module's scope is the estate's resource group");
        Of(resources, "Microsoft.ManagedIdentity/userAssignedIdentities").Single()["name"]!.GetValue<string>().Should().StartWith("id-health-check-");
        File.ReadAllText(Path.Combine(RepositoryRoot.Path, McpModule)).Should().NotContain("roleAssignments", "the Agent's side holds no Azure role: Reader belongs to the app");
    }

    [Fact]
    public void TheAppsRegistration_HasTheTwoOperatorRoles_ForUsersOnly_AndEntraSignsInNobodyWithoutOne()
    {
        var resources = AppResources();
        var app = Of(resources, "Microsoft.Graph/applications@v1.0").Single()["properties"]!;

        app["displayName"]!.GetValue<string>().Should().Be("Norvia Estate health check (env)");
        var roles = app["appRoles"]!.AsArray().Select(role => role!).ToList();
        roles.Select(role => role["value"]!.GetValue<string>()).Should().Equal("HealthCheck.Run", "HealthCheck.Read");
        roles.SelectMany(role => role["allowedMemberTypes"]!.AsArray().Select(type => type!.GetValue<string>())).Should().OnlyContain(type => type == "User");
        Of(resources, "Microsoft.Graph/servicePrincipals@v1.0").Single()["properties"]!["appRoleAssignmentRequired"]!.GetValue<bool>().Should().BeTrue();
        Of(resources, "Microsoft.Graph/appRoleAssignedTo@v1.0").Select(grant => grant["properties"]!["principalId"]!.GetValue<string>()).Should().OnlyContain(id => id == Presenter);
    }

    [Fact]
    public void TheAppsRegistration_TakesTheSignInCallbacksThePostDeployHookStores()
    {
        var app = Of(AppResources("param healthCheckRedirectUris = 'https://a.example/.auth/login/aad/callback'"), "Microsoft.Graph/applications@v1.0").Single()["properties"]!;

        app["web"]!["redirectUris"]!.AsArray().Select(uri => uri!.GetValue<string>()).Should().Equal("https://a.example/.auth/login/aad/callback");
        Script("register-sign-in-callbacks.sh").Should().Contain("ESTATE_HEALTH_CHECK_REDIRECT_URIS");
    }

    [Fact]
    public void TheEstate_CallsTheModules_GivesTheAppFoundryUser_AndStaysWithinItsOutputs()
    {
        var estate = File.ReadAllText(Path.Combine(RepositoryRoot.Path, "infra/estate.bicep"));

        estate.Should().Contain("module healthCheck 'estate/health-check.bicep'").And.Contain("module healthCheckMcp 'estate/health-check-mcp.bicep'");
        estate.Should().Contain("healthCheck.outputs.principalId");
        Regex.Count(estate, @"(?m)^output ").Should().BeLessThanOrEqualTo(64, "a template may have 64 outputs");
    }

    [Fact]
    public void TheAppHost_RunsTheAppAsItsOwnIdentity_BehindEasyAuth_WithItsAgentApiOutsideTheGate()
    {
        var host = File.ReadAllText(Path.Combine(RepositoryRoot.Path, "src/Norvia.AppHost/AppHost.cs"));
        var block = host[host.IndexOf("healthCheck.WithExternalHttpEndpoints()", StringComparison.Ordinal)..];
        block = block[..block.IndexOf(";\n", block.IndexOf("EntraSignIn.For", StringComparison.Ordinal), StringComparison.Ordinal)];

        block.Should().Contain("WithAzureUserAssignedIdentity(healthCheckIdentity)").And.Contain("estate.HealthCheckClientId");
        block.Should().Contain("[\"/agent/v1/*\"]").And.Contain("Agents__Api__Audience").And.Contain("Agents__Api__GatewayClientId");
        block.Should().Contain("Estate__ResourceGroupId").And.Contain("Estate__ServiceBusNamespace").And.Contain("Agents__Foundry__ProjectEndpoint");
    }

    [Fact]
    public void DeployScript_PointsTheHealthCheckBackendAtTheApp_AndKeepsTheAddress()
    {
        var script = Script("deploy-agents.sh");

        script.Should().Contain("health-check-web").And.Contain("health-check-agent-api").And.Contain("ESTATE_HEALTH_CHECK_BASE_URL");
        Script("provision-estate.sh").Should().Contain("Norvia Estate health check (").And.Contain("id-health-check-").And.Contain("estate_health_check_agent_api_client_id");
    }

    [Fact]
    public void SmokeRun_ChecksTheRoles_TheFindingComingAndGoing_TheHopsOnTheTrail_AndThatNothingIsKept()
    {
        var script = Script("smoke-test-estate-health-check.sh");

        script.Should().Contain("./as-found.sh as-found").And.Contain("./as-found.sh fixed");
        script.IndexOf("./as-found.sh as-found", StringComparison.Ordinal).Should().BeLessThan(script.IndexOf("./as-found.sh fixed", StringComparison.Ordinal));
        script.Should().Contain("F-DLQ-ALERT").And.Contain("carrier-tracking").And.Contain("agent-mcp-health-check").And.Contain("agent-view-health-check").And.Contain("store: false");
        script.Should().Contain("holds no Azure role").And.Contain("holds Reader on the estate's resource group").And.Contain("is the only role on the app's agent API");
        script.Should().Contain("structured_inputs: {correlationId: $run}");
    }

    [Fact]
    public void SmokeRun_NeverEchoesATokenOrASecret()
    {
        var lines = Script("smoke-test-estate-health-check.sh").Split('\n').Where(line => line.TrimStart().StartsWith("echo", StringComparison.Ordinal));

        lines.Should().NotContain(line => line.Contains("token", StringComparison.OrdinalIgnoreCase) || line.Contains("SECRET", StringComparison.Ordinal));
    }

    [Fact]
    public void TheAgentsDefinition_NamesTheContractsServer_AndOnlyItsTools()
    {
        var definition = File.ReadAllText(Path.Combine(RepositoryRoot.Path, "agents/estate-health-check.agent.yaml"));
        var tools = ContractFile["tools"]!.AsArray().Select(tool => tool!["name"]!.GetValue<string>()).ToList();

        definition.Should().Contain($"server: {ContractFile["server"]!.GetValue<string>()}");
        foreach (var tool in tools)
        {
            definition.Should().Contain($"- {tool}");
        }

    }
}
