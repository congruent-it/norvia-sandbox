using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Norvia.Tms.ArchitectureTests;

namespace Norvia.Erp.Mcp.IntegrationTests.FinanceAgent;

/// <summary>
/// The Carrier invoice matching agent's two MCP servers (ADR 0040, 0041): the API Management one (the estate's Bicep, evaluated locally with <c>bicep snapshot</c>) and
/// the local stand-in (running in memory). The policy is checked with the finance server's own values (its contract's role and tools, the rate limit its module call gives);
/// the generic module's cases are in the architecture tests. API Management runs only in Azure, so the post-deploy smoke run calls the deployed server.
/// </summary>
public sealed class FinanceMcpServerTests
{
    private const string McpModule = "infra/estate/finance-matching-mcp.bicep";
    private const string ServerModule = "infra/estate/agent-mcp-server.bicep";
    private const string Contract = "contracts/agent-mcp/finance-matching.tools.json";

    // The app registrations' client IDs exist only once the template is deployed, so the policy is evaluated from the server module with these.
    private const string ViewAudience = "22222222-2222-2222-2222-222222222222";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static string Text(string path) => File.ReadAllText(Path.Combine(RepositoryRoot.Path, path));

    private static JsonNode ContractFile() => JsonNode.Parse(Text(Contract))!;

    private static JsonArray ContractTools() => ContractFile()["tools"]!.AsArray();

    private static int RateLimitOfTheModuleCall() => int.Parse(Regex.Match(Text(McpModule), @"rateLimitCalls: (\d+)").Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);

    private static List<JsonNode> ServerResources() =>
        BicepSnapshot.Resources(
            ServerModule,
            "param apiManagementName = 'apim'",
            "param serverName = 'finance-matching'",
            "param purpose = 'p'",
            $"param tools = {BicepArray(ContractTools())}",
            $"param audience = '{ViewAudience}'",
            $"param requiredRole = '{ContractFile()["role"]!.GetValue<string>()}'",
            $"param rateLimitCalls = {RateLimitOfTheModuleCall()}",
            "param tenantId = 'tenant-1'");

    // A parameters file takes Bicep, not JSON: single-quoted strings and one property to a line.
    private static string BicepArray(JsonArray tools) =>
        "[" + string.Join(' ', tools.Select(tool => "{\n" + string.Join('\n', tool!.AsObject().Select(property => $"  {property.Key}: '{property.Value!.GetValue<string>().Replace("\\", "\\\\", StringComparison.Ordinal).Replace("'", "\\'", StringComparison.Ordinal)}'")) + "\n}")) + "]";

    private static XElement Inbound() =>
        XElement.Parse(ServerResources().Single(resource => resource["type"]!.GetValue<string>() == "Microsoft.ApiManagement/service/apis/policies")["properties"]!["value"]!.GetValue<string>())
            .Element("inbound")!;

    private static XElement Refusal(XElement inbound, string failureCode) =>
        inbound.Elements("choose").Elements("when").Single(when => when.Element("trace")?.Elements("metadata").Any(metadata => metadata.Attribute("name")!.Value == "failureCode" && metadata.Attribute("value")!.Value == failureCode) == true);

    [Fact]
    public async Task ToolsList_OfTheStandIn_IsTheToolsListOfTheBicep_NameDescriptionAndRoute()
    {
        await using var standIn = new StandInHost();
        var standInTools = await standIn.ListToolsAsync(Ct);
        var resources = BicepSnapshot.Resources(
            McpModule,
            "param environmentName = 'env'",
            "param apiManagementName = 'apim'",
            "param apiManagementPrincipalId = '00000000-0000-0000-0000-000000000001'",
            "param apiManagementClientId = '00000000-0000-0000-0000-000000000002'");
        var bicepTools = resources.Where(resource => resource["type"]!.GetValue<string>() == "Microsoft.ApiManagement/service/apis/tools").ToList();

        standInTools.Select(tool => tool!["name"]!.GetValue<string>())
            .Should().Equal(bicepTools.Select(tool => tool["properties"]!["displayName"]!.GetValue<string>()), "the stand-in serves the tools of the Bicep, and no others");
        standInTools.Select(tool => tool!["description"]!.GetValue<string>())
            .Should().Equal(bicepTools.Select(tool => tool["properties"]!["description"]!.GetValue<string>()));
        standInTools.Select(tool => tool!["name"]!.GetValue<string>())
            .Should().Equal(ContractTools().Select(tool => tool!["name"]!.GetValue<string>()), "both read the contract file");
    }

    [Fact]
    public void ValidToken_IsAnEntraTokenForTheAgentViewAudience_OfTheTenant_AndNoOtherAudience()
    {
        var validate = Inbound().Element("validate-azure-ad-token")!;

        validate.Attribute("tenant-id")!.Value.Should().Be("tenant-1");
        validate.Element("audiences")!.Elements("audience").Select(audience => audience.Value).Should().Equal(ViewAudience);
        validate.Attribute("ignore-error").Should().BeNull("a token that fails the check is answered 401, never let through");
    }

    [Fact]
    public void ATokenWithoutTheContractsRole_IsRefusedWith403()
    {
        var refusal = Refusal(Inbound(), "AgentRoleMissing");

        refusal.Attribute("condition")!.Value.Should().Contain($"\"{ContractFile()["role"]!.GetValue<string>()}\"");
        refusal.Element("return-response")!.Element("set-status")!.Attribute("code")!.Value.Should().Be("403");
    }

    [Fact]
    public void TheRateLimit_IsTheModuleCalls_CountedPerAgentIdentity()
    {
        var limit = Inbound().Element("rate-limit-by-key")!;

        limit.Attribute("calls")!.Value.Should().Be(RateLimitOfTheModuleCall().ToString(System.Globalization.CultureInfo.InvariantCulture));
        limit.Attribute("counter-key")!.Value.Should().Contain("agentId");
    }

    [Fact]
    public void ACallToAToolOutsideTheContract_IsRefusedWith403_AndTheListIsTheContractsTools()
    {
        var refusal = Refusal(Inbound(), "ToolNotOnList");
        var names = string.Join('|', ContractTools().Select(tool => tool!["name"]!.GetValue<string>()));

        refusal.Attribute("condition")!.Value.Should().Contain($"\"{names}\".Split('|')");
        refusal.Element("return-response")!.Element("set-status")!.Attribute("code")!.Value.Should().Be("403");
    }
}
