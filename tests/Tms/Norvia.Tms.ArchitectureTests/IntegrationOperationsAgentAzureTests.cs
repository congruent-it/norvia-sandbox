using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Norvia.Tms.ArchitectureTests;

/// <summary>
/// The Integration operations agent in Azure (#380, ADR 0040): the API Management MCP server over the console's agent API, the one role
/// on each of its two app registrations, and the scripts that publish the Agent and smoke-test it. The Bicep is evaluated locally with
/// <c>bicep snapshot</c>; nothing is deployed. The tool-list drift test is in <see cref="AgentMcpServerTests"/> (the Bicep reads the contract
/// file) and in the stand-in's unit tests (the stand-in serves that file), so the stand-in's list is the Bicep's.
/// </summary>
public sealed class IntegrationOperationsAgentAzureTests
{
    private const string Module = "infra/estate/integration-operations-mcp.bicep";
    private const string Contract = "contracts/agent-mcp/integration-operations.tools.json";
    private const string ApimPrincipal = "00000000-0000-0000-0000-000000000001";
    private const string ApimClient = "00000000-0000-0000-0000-000000000002";

    private static readonly JsonNode ContractFile = JsonNode.Parse(File.ReadAllText(Path.Combine(RepositoryRoot.Path, Contract)))!;

    private static List<JsonNode> Resources(params string[] extra) =>
        BicepSnapshot.Resources(
            Module,
            [
                "param environmentName = 'env'", "param apiManagementName = 'apim'", $"param apiManagementPrincipalId = '{ApimPrincipal}'",
                $"param apiManagementClientId = '{ApimClient}'", .. extra,
            ]);

    private static IEnumerable<JsonNode> Of(List<JsonNode> resources, string type) => resources.Where(resource => resource["type"]!.GetValue<string>() == type);

    private static IEnumerable<JsonNode> Applications(List<JsonNode> resources) => Of(resources, "Microsoft.Graph/applications@v1.0");

    private static string Script(string name) => File.ReadAllText(Path.Combine(RepositoryRoot.Path, "infra", name));

    [Fact]
    public void TheMcpServer_HasTheContractsTools_AndNoOthers()
    {
        var resources = Resources();
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
    }

    [Fact]
    public void TheMcpServer_IsAtAgentsIntegrationOperations_WithTheEntraTokenAsItsOnlyCredential()
    {
        var mcp = Of(Resources(), "Microsoft.ApiManagement/service/apis").Single(api => api["properties"]!["type"]?.GetValue<string>() == "mcp");

        mcp["properties"]!["path"]!.GetValue<string>().Should().Be("agents/integration-operations");
        mcp["properties"]!["subscriptionRequired"]!.GetValue<bool>().Should().BeFalse();
    }

    // The app registrations' client IDs are known only when the template is deployed, so the policy is not in the snapshot. The module's own
    // tests (AgentMcpServerTests) cover the policy with the module's values filled in; this checks the call gives it the right ones.
    [Fact]
    public void TheModuleCall_GivesThePolicyTheContractsTools_TheContractsRole_TheAgentViewAsAudience_AndTheConsolesAgentApiAsBackend()
    {
        var bicep = File.ReadAllText(Path.Combine(RepositoryRoot.Path, Module));

        bicep.Should().Contain($"loadJsonContent('../../{Contract}')");
        bicep.Should().Contain("tools: contract.tools").And.Contain("requiredRole: role").And.Contain("audience: view.appId");
        bicep.Should().Contain("backendId: consoleBackend.name").And.Contain("backendAudience: consoleAgentApi.appId").And.Contain("apiManagementIdentityClientId: apiManagementClientId");
        bicep.Should().Contain("rateLimitCalls:", "the Agent has a rate limit of its own");
    }

    [Fact]
    public void TheBackend_IsTheConsolesAgentApi_APlaceholderUntilTheConsoleIsDeployed_AndThenItsAddress()
    {
        Of(Resources(), "Microsoft.ApiManagement/service/backends").Single()["properties"]!["url"]!.GetValue<string>().Should().Be("https://integration-console.invalid/agent/v1");
        Of(Resources("param consoleBaseUrl = 'https://integration-console-web.example.io'"), "Microsoft.ApiManagement/service/backends").Single()["properties"]!["url"]!.GetValue<string>()
            .Should().Be("https://integration-console-web.example.io/agent/v1");
    }

    [Fact]
    public void BothAppRegistrations_HaveOneRole_TheContractsRead_ForApplicationsOnly()
    {
        var apps = Applications(Resources()).Select(app => app["properties"]!).ToList();

        apps.Should().HaveCount(2);
        foreach (var app in apps)
        {
            var roles = app["appRoles"]!.AsArray().Select(role => role!).ToList();
            roles.Should().ContainSingle();
            roles[0]["value"]!.GetValue<string>().Should().Be("IntegrationConsole.Agent.Read");
            roles[0]["allowedMemberTypes"]!.AsArray().Select(type => type!.GetValue<string>()).Should().Equal("Application");
            app["api"]!["requestedAccessTokenVersion"]!.GetValue<int>().Should().Be(2, "azp is the caller's client ID in a v2 token");
        }

        apps.Select(app => app["displayName"]!.GetValue<string>()).Should().Contain("Norvia integration-operations agent view (env)", "the scripts look the view up by this name");
        ContractFile["role"]!.GetValue<string>().Should().Be("IntegrationConsole.Agent.Read");
    }

    [Fact]
    public void EntraIssuesNoTokenForEitherApi_ToAnIdentityWithoutTheRole()
    {
        var principals = Of(Resources(), "Microsoft.Graph/servicePrincipals@v1.0").ToList();

        principals.Should().HaveCount(2).And.OnlyContain(principal => principal["properties"]!["appRoleAssignmentRequired"]!.GetValue<bool>());
    }

    [Fact]
    public void OnlyApiManagementsIdentity_IsGivenARole_InTheBicep_AndTheAgentsIdentitiesAreGivenTheirsByTheScript()
    {
        var assignments = Of(Resources(), "Microsoft.Graph/appRoleAssignedTo@v1.0").ToList();

        assignments.Should().ContainSingle().Which["properties"]!["principalId"]!.GetValue<string>().Should().Be(ApimPrincipal);
        Script("assign-agent-roles.ps1").Should().Contain("appRoleAssignedTo");
    }

    [Fact]
    public void TheEstate_CallsTheModule_AndStaysWithinItsOutputs()
    {
        var estate = File.ReadAllText(Path.Combine(RepositoryRoot.Path, "infra/estate.bicep"));

        estate.Should().Contain("module integrationOperationsMcp 'estate/integration-operations-mcp.bicep'");
        Regex.Count(estate, @"(?m)^output ").Should().BeLessThanOrEqualTo(64, "a template may have 64 outputs");
    }

    [Fact]
    public void TheApplicationsIdentities_HoldFoundryUserOnTheNorviaProjectOnly()
    {
        var foundry = File.ReadAllText(Path.Combine(RepositoryRoot.Path, "infra/estate/foundry.bicep"));
        var estate = File.ReadAllText(Path.Combine(RepositoryRoot.Path, "infra/estate.bicep"));

        Regex.Match(foundry, @"(?ms)^resource applicationsUseTheProject .*?^\}\]").Value.Should().Contain("scope: project").And.NotContain("intakeProject");
        foundry.Should().NotContain("for principalId in applicationPrincipalIds: {\n  name: guid(intakeProject.id");
        estate.Should().Contain("applicationPrincipalIds: [identities.outputs.integrationConsolePrincipalId, healthCheck.outputs.principalId, tmsUiIdentity.properties.principalId, identities.outputs.simulatorPrincipalId]");
    }

    [Fact]
    public void DeployScript_PublishesEveryDefinitionAsAPromptAgent_WithTheReadOnlyMcpToolAndTheCorrelationHeader()
    {
        var script = Script("deploy-agents.ps1");

        script.Should().MatchRegex(@"kind\s*=\s*'prompt'").And.MatchRegex(@"require_approval\s*=\s*'never'").And.Contain("project_connection_id").And.Contain("{{correlationId}}");
        script.Should().MatchRegex(@"allowed_tools\s*=\s*@\(\$definition\.tools\.names\)").And.Contain("correlationIdHeader").And.Contain("*.agent.yaml");
        script.Should().Contain("create-agent-connection.ps1").And.Contain("assign-agent-roles.ps1");
        script.LastIndexOf("create-agent-connection.ps1", StringComparison.Ordinal).Should().BeLessThan(script.IndexOf("/versions?api-version", StringComparison.Ordinal));
        script.IndexOf("/versions?api-version", StringComparison.Ordinal).Should().BeLessThan(script.LastIndexOf("infra/assign-agent-roles.ps1", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("deploy-agents.ps1")]
    [InlineData("smoke-test-integration-operations-agent.ps1")]
    public void Scripts_NeverEchoATokenOrASecret(string name)
    {
        var lines = Script(name).Split('\n').Where(line => line.TrimStart().StartsWith("echo", StringComparison.Ordinal) || line.TrimStart().StartsWith("Write-", StringComparison.Ordinal));

        lines.Should().NotContain(line => line.Contains("token", StringComparison.OrdinalIgnoreCase) || line.Contains("SECRET", StringComparison.Ordinal));
    }

    [Fact]
    public void DeployScript_RunsLastInAzdsPostdeployHook_AfterTheAppsAreDeployed()
    {
        var postdeploy = Script("postdeploy.ps1");

        postdeploy.Should().Contain("deploy-agents.ps1");
        postdeploy.IndexOf("deploy-agents.ps1", StringComparison.Ordinal).Should().BeGreaterThan(postdeploy.IndexOf("register-sign-in-callbacks.ps1", StringComparison.Ordinal));
    }

    [Fact]
    public void SmokeRun_ChecksTheRoles_TheAnswer_TheHopsOnTheTrail_AndThatNothingIsKept()
    {
        var script = Script("smoke-test-integration-operations-agent.ps1");

        script.Should().Contain("UnmappableTrackingMessage").And.Contain("get_message_trail").And.Contain("agent-mcp-integration-operations").And.Contain("agent-view-integration-operations");
        script.Should().Contain("hold exactly $role on the agent view").And.Contain("is the only role on the console's agent API").And.MatchRegex(@"store\s*=\s*\$false");
        script.Should().MatchRegex(@"structured_inputs\s*=\s*@\{\s*correlationId\s*=\s*\$Trail\s*\}");
    }

    [Fact]
    public void AdrsAAndB_AreAccepted()
    {
        foreach (var adr in new[] { "0040-agents-read-the-estate-through-api-management-have-no-write-tools-and-their-own-identity.md", "0041-locally-an-in-process-runner-and-an-mcp-stand-in-play-foundry-and-api-managements-mcp-server.md" })
        {
            File.ReadAllText(Path.Combine(RepositoryRoot.Path, "docs/adr", adr)).Should().MatchRegex(@"\*\*Status: Accepted");
        }
    }
}
