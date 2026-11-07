using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Norvia.Tms.ArchitectureTests;

/// <summary>
/// The Carrier invoice rejection reason agent in Azure (#473, ADR 0040): the boundary is the tool. Its MCP server has the one tool of the
/// contract, an agent view app registration of its own with a role of its own, and no role on the Finance agent view. The Bicep is
/// evaluated locally with <c>bicep snapshot</c>; nothing is deployed.
/// </summary>
public sealed class FinanceRejectionReasonAgentAzureTests
{
    private const string McpModule = "infra/estate/finance-rejection-reason-mcp.bicep";
    private const string Contract = "contracts/agent-mcp/finance-rejection-reason.tools.json";
    private const string Role = "CarrierInvoices.FinanceRejectionReasonAgent.Read";

    private static readonly string[] ApplicationOnly = ["Application"];

    private static readonly JsonNode ContractFile = JsonNode.Parse(File.ReadAllText(Path.Combine(RepositoryRoot.Path, Contract)))!;

    private static List<JsonNode> Resources(params string[] extra) =>
        BicepSnapshot.Resources(
            McpModule,
            ["param environmentName = 'env'", "param apiManagementName = 'apim'", "param apiManagementClientId = '00000000-0000-0000-0000-000000000002'", "param erpAgentApiAppId = '00000000-0000-0000-0000-000000000003'", .. extra]);

    private static IEnumerable<JsonNode> Of(List<JsonNode> resources, string type) => resources.Where(resource => resource["type"]!.GetValue<string>() == type);

    [Fact]
    public void TheMcpServer_HasTheOneToolOfTheContract_AndNoOthers()
    {
        var tools = Of(Resources(), "Microsoft.ApiManagement/service/apis/tools").ToList();

        tools.Select(tool => tool["properties"]!["displayName"]!.GetValue<string>()).Should().Equal("get_carrier_visible_line_facts");
        tools.Select(tool => tool["properties"]!["description"]!.GetValue<string>()).Should().Equal(ContractFile["tools"]!.AsArray().Select(tool => tool!["description"]!.GetValue<string>()));
        Of(Resources(), "Microsoft.ApiManagement/service/apis/operations").Select(operation => operation["properties"]!["method"]!.GetValue<string>()).Should().OnlyContain(method => method == "GET");
    }

    [Fact]
    public void TheView_IsAnAppRegistrationOfItsOwn_WithTheContractsRoleAndNoOther_ForApplicationsOnly()
    {
        var app = Of(Resources(), "Microsoft.Graph/applications@v1.0").Single()["properties"]!;

        app["displayName"]!.GetValue<string>().Should().Be("Norvia finance-rejection-reason agent view (env)", "assign-agent-roles.ps1 looks the view up by this name");
        var roles = app["appRoles"]!.AsArray().Select(role => role!).ToList();
        roles.Select(role => role["value"]!.GetValue<string>()).Should().Equal(Role);
        roles.Should().OnlyContain(role => role["allowedMemberTypes"]!.AsArray().Select(type => type!.GetValue<string>()).SequenceEqual(ApplicationOnly));
        Of(Resources(), "Microsoft.Graph/servicePrincipals@v1.0").Single()["properties"]!["appRoleAssignmentRequired"]!.GetValue<bool>().Should().BeTrue("Entra issues no token to an identity without the role");
        ContractFile["role"]!.GetValue<string>().Should().Be(Role);
    }

    [Fact]
    public void TheBackend_IsTheRejectionReasonAgentView_APlaceholderUntilTheErpIsDeployed_AndThenItsAddress()
    {
        Of(Resources(), "Microsoft.ApiManagement/service/backends").Single()["properties"]!["url"]!.GetValue<string>().Should().Be("https://erp.invalid/v1/agent/finance-rejection-reason");
        Of(Resources("param erpBaseUrl = 'https://erp-api.example.io'"), "Microsoft.ApiManagement/service/backends").Single()["properties"]!["url"]!.GetValue<string>()
            .Should().Be("https://erp-api.example.io/v1/agent/finance-rejection-reason");
    }

    [Fact]
    public void TheModuleCall_GivesThePolicyTheContractsTool_TheContractsRole_ItsOwnViewAsAudience_AndTheErpAgentApiAsBackend()
    {
        var bicep = File.ReadAllText(Path.Combine(RepositoryRoot.Path, McpModule));

        bicep.Should().Contain($"loadJsonContent('../../{Contract}')").And.Contain("tools: contract.tools").And.Contain("requiredRole: role").And.Contain("audience: view.appId");
        bicep.Should().Contain("backendId: erpBackend.name").And.Contain("backendAudience: erpAgentApiAppId").And.MatchRegex(@"rateLimitCalls: \d+");
        bicep.Should().NotContain("finance-matching.tools.json", "the rejection reason agent's door shares no tool with the matching agent's");
    }

    [Fact]
    public void TheRejectionReasonAgentsRole_IsOnTheErpAgentApi_AndTheFinanceAgentViewRequiresOnlyTheOther()
    {
        var matching = File.ReadAllText(Path.Combine(RepositoryRoot.Path, "infra/estate/finance-matching-mcp.bicep"));

        matching.Should().Contain("finance-rejection-reason.tools.json").And.Contain("output erpAgentApiAppId");
        File.ReadAllText(Path.Combine(RepositoryRoot.Path, "src/Erp/Norvia.Erp.Api/Agents/FinanceAgentEndpoints.cs")).Should().Contain("RequireRole(AgentRoles.FinanceRead)").And.NotContain("FinanceRejectionReasonRead");
    }

    [Fact]
    public void TheEstateAndTheDeployScript_WireTheServer()
    {
        var estate = File.ReadAllText(Path.Combine(RepositoryRoot.Path, "infra/estate.bicep"));

        estate.Should().Contain("module financeRejectionReasonMcp 'estate/finance-rejection-reason-mcp.bicep'").And.Contain("erpAgentApiAppId: financeMatchingMcp.outputs.erpAgentApiAppId");
        Regex.Count(estate, @"(?m)^output ").Should().BeLessThanOrEqualTo(64, "a template may have 64 outputs");
        File.ReadAllText(Path.Combine(RepositoryRoot.Path, "infra/deploy-agents.ps1")).Should().Contain("'finance-rejection-reason'").And.Contain("/v1/agent/finance-rejection-reason");
    }
}
