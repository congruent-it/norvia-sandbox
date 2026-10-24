using System.Text.Json.Nodes;

namespace Norvia.Tms.ArchitectureTests;

/// <summary>
/// The Foundry resources of the estate (infra/estate/foundry.bicep): the model keeps processing in the EU data zone and is capped,
/// everything is tagged for cost, Application Insights is connected and no content recording is set. The Bicep is evaluated locally with
/// <c>bicep snapshot</c>; nothing is deployed.
/// </summary>
public sealed class FoundryEstateTests
{
    private const string Foundry = "infra/estate/foundry.bicep";
    private const string Estate = "infra/estate.bicep";
    private const string Presenter = "00000000-0000-0000-0000-000000000009";

    [Fact]
    public void ModelDeployment_IsDataZoneStandard_NeverGlobalStandard()
    {
        var deployment = Single(Resources(), "Microsoft.CognitiveServices/accounts/deployments");

        deployment["sku"]!["name"]!.GetValue<string>().Should().Be("DataZoneStandard");
        deployment["properties"]!["model"]!["format"]!.GetValue<string>().Should().Be("OpenAI");
        File.ReadAllText(Path.Combine(RepositoryRoot.Path, Foundry)).Should().NotContain("GlobalStandard'");
    }

    [Fact]
    public void ModelDeployment_HasAQuotaCap()
    {
        Single(Resources(), "Microsoft.CognitiveServices/accounts/deployments")["sku"]!["capacity"]!.GetValue<int>().Should().Be(30);
        Single(Resources("param modelCapacity = 10"), "Microsoft.CognitiveServices/accounts/deployments")["sku"]!["capacity"]!.GetValue<int>().Should().Be(10);
    }

    [Fact]
    public void ModelNotDeployed_RemovesOnlyTheModelDeployment()
    {
        var healthy = Types(Resources());
        var switchedOff = Types(Resources("param modelDeployed = false"));

        healthy.Except(switchedOff).Should().Equal("Microsoft.CognitiveServices/accounts/deployments");
        switchedOff.Except(healthy).Should().BeEmpty();
    }

    [Fact]
    public void EveryFoundryResource_CarriesTheCostTags_AndIsPublicWithEntraOnly()
    {
        var resources = Resources().Where(resource => Type(resource).StartsWith("Microsoft.CognitiveServices/accounts", StringComparison.Ordinal) && !Type(resource).EndsWith("connections", StringComparison.Ordinal)).ToList();

        resources.Should().HaveCount(3);
        resources.Should().OnlyContain(resource => resource["tags"]!["workload"]!.GetValue<string>() == "foundry-agents" && resource["tags"]!["cost-category"]!.GetValue<string>() == "ai-model");
        var account = Single(Resources(), "Microsoft.CognitiveServices/accounts");
        account["kind"]!.GetValue<string>().Should().Be("AIServices");
        account["properties"]!["disableLocalAuth"]!.GetValue<bool>().Should().BeTrue();
        account["properties"]!["publicNetworkAccess"]!.GetValue<string>().Should().Be("Enabled");
        account["properties"]!["networkAcls"].Should().BeNull("there is no VNet");
    }

    [Fact]
    public void ApplicationInsightsIsConnected_AndNothingRecordsContent()
    {
        var connection = Single(Resources(), "Microsoft.CognitiveServices/accounts/connections");

        connection["properties"]!["category"]!.GetValue<string>().Should().Be("AppInsights");
        File.ReadAllText(Path.Combine(RepositoryRoot.Path, Foundry)).ToLowerInvariant().Should().NotContain("capture_message_content").And.NotContain("recordcontent");
    }

    [Fact]
    public void AgentsEnabled_DefaultsOff_InTheEstateAndInTheProjectTag()
    {
        File.ReadAllText(Path.Combine(RepositoryRoot.Path, Estate)).Should().Contain("param agentsEnabled bool = false");
        Single(Resources(), "Microsoft.CognitiveServices/accounts/projects")["tags"]!["agents-enabled"]!.GetValue<string>().Should().Be("false");
        Single(Resources("param agentsEnabled = true"), "Microsoft.CognitiveServices/accounts/projects")["tags"]!["agents-enabled"]!.GetValue<string>().Should().Be("true");
    }

    [Fact]
    public void FoundryUsers_GetTheFoundryUserRoleOnTheProject()
    {
        var assignments = Resources("param foundryUserPrincipalIds = ['" + Presenter + "', '00000000-0000-0000-0000-000000000008']")
            .Where(resource => Type(resource) == "Microsoft.Authorization/roleAssignments").ToList();

        assignments.Should().HaveCount(2);
        assignments.Should().OnlyContain(assignment => assignment["properties"]!["roleDefinitionId"]!.GetValue<string>().EndsWith("53ca6127-db72-4b80-b1b0-d745d6d5456d", StringComparison.Ordinal));
        Resources().Should().NotContain(resource => Type(resource) == "Microsoft.Authorization/roleAssignments");
    }

    private static readonly string[] Defaults =
        ["param location = 'swedencentral'", "param tags = {}", "param applicationInsightsName = 'appi'", "param modelName = 'gpt-5.4-mini'", "param modelVersion = '2026-03-17'",
            "param modelDeployed = true", "param modelCapacity = 30", "param agentsEnabled = false", "param foundryUserPrincipalIds = []"];

    // A parameter line passed in replaces the default of the same name.
    private static List<JsonNode> Resources(params string[] parameters) => BicepSnapshot.Resources(Foundry,
        [.. Defaults.Where(line => !parameters.Any(given => given.Split(' ')[1] == line.Split(' ')[1])), .. parameters]);

    private static string Type(JsonNode resource) => resource["type"]!.GetValue<string>();

    private static List<string> Types(List<JsonNode> resources) => resources.Select(Type).Distinct().Order().ToList();

    private static JsonNode Single(List<JsonNode> resources, string type) => resources.Single(resource => Type(resource) == type);
}
