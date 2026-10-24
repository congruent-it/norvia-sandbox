using System.Text.Json.Nodes;

namespace Norvia.Tms.ArchitectureTests;

/// <summary>
/// The conversation sweeper (infra/estate/conversation-sweeper-logic-app.bicep, src/Integration/ConversationSweeperLogicApp): it holds only
/// the Foundry role it needs, the as-found toggle stops it, and its workflow deletes with its own identity and fails with
/// <c>ConversationNotDeleted</c>. The Bicep is evaluated locally with <c>bicep snapshot</c>; nothing is deployed, and the Foundry calls
/// are made only by the post-deploy smoke run (infra/smoke-test-conversation-sweeper.sh).
/// </summary>
public sealed class ConversationSweeperTests
{
    private const string Module = "infra/estate/conversation-sweeper-logic-app.bicep";
    private const string FoundryUser = "53ca6127-db72-4b80-b1b0-d745d6d5456d";
    private static readonly string Root = RepositoryRoot.Path;

    [Fact]
    public void Sweeper_HoldsTheFoundryUserRoleOnTheProject_AndNoOtherFoundryRole()
    {
        var assignments = Resources().Where(resource => Type(resource) == "Microsoft.Authorization/roleAssignments").ToList();

        var foundry = assignments.Where(assignment => assignment["properties"]!["roleDefinitionId"]!.GetValue<string>().EndsWith(FoundryUser, StringComparison.Ordinal)).ToList();
        foundry.Should().ContainSingle();
        foundry[0]["name"]!.GetValue<string>().Should().NotBeNullOrEmpty();
        File.ReadAllText(Path.Combine(Root, Module)).Should().Contain("scope: foundryProject", "the role is on the project, never the account or the resource group");
    }

    [Fact]
    public void LogicApp_RunsEveryNightOnTheSharedPlan_AndIsTaggedForTheSmokeRun()
    {
        var app = Single(Resources(), "Microsoft.Web/sites");

        app["kind"]!.GetValue<string>().Should().Be("functionapp,workflowapp");
        app["tags"]!["workload"]!.GetValue<string>().Should().Be("conversation-sweeper");
        app["properties"]!["enabled"]!.GetValue<bool>().Should().BeTrue();
    }

    [Fact]
    public void NoConversationSweeper_BuildsTheLogicAppStopped_AndNothingElseChanges()
    {
        var healthy = Resources();
        var asFound = Resources("param enabled = false");

        Single(asFound, "Microsoft.Web/sites")["properties"]!["enabled"]!.GetValue<bool>().Should().BeFalse();
        asFound.Select(Type).Order().Should().Equal(healthy.Select(Type).Order());
    }

    [Fact]
    public void Estate_HasTheToggle_HealthyByDefault_InEveryPreset_AndOnlyTheAsFoundPresetSetsIt()
    {
        File.ReadAllText(Path.Combine(Root, "infra", "estate.bicep")).Should().Contain("noConversationSweeper: false").And.Contain("enabled: !(asFound.?noConversationSweeper ?? false)");
        foreach (var file in Directory.GetFiles(Path.Combine(Root, "infra", "presets"), "*.json"))
        {
            var preset = JsonNode.Parse(File.ReadAllText(file))!;

            preset["noConversationSweeper"]!.GetValue<bool>().Should().Be(Path.GetFileName(file) == "as-found.json", Path.GetFileName(file));
        }
    }

    [Fact]
    public void Workflow_RunsNightly_DeletesAsItsOwnIdentity_AndFailsWithTheCode()
    {
        var workflow = Workflow();
        var definition = workflow["definition"]!;

        definition["triggers"]!["Every_night"]!["type"]!.GetValue<string>().Should().Be("Recurrence");
        definition["triggers"]!["Every_night"]!["recurrence"]!["frequency"]!.GetValue<string>().Should().Be("Day");
        var calls = Descendants(definition).Where(node => node["type"]?.GetValue<string>() == "Http").Select(node => node["inputs"]!).ToList();
        calls.Select(call => call["method"]!.GetValue<string>()).Distinct().Order().Should().Equal("DELETE", "GET");
        calls.Should().OnlyContain(call => call["authentication"]!["type"]!.GetValue<string>() == "ManagedServiceIdentity"
            && call["authentication"]!["identity"]!.GetValue<string>() == "@parameters('sweeperIdentityId')"
            && call["authentication"]!["audience"]!.GetValue<string>() == "https://ai.azure.com");
        calls.Select(call => call["uri"]!.GetValue<string>()).Should().OnlyContain(uri => uri.StartsWith("@{parameters('foundryProjectEndpoint')}/openai/v1/", StringComparison.Ordinal));
        Descendants(definition).Single(node => node["type"]?.GetValue<string>() == "Terminate")["inputs"]!["runError"]!["code"]!.GetValue<string>().Should().Be("ConversationNotDeleted");
    }

    [Fact]
    public void Workflow_SweepsConversationsAndResponses_OlderThanTheCutoff_FromTheFirstPage()
    {
        var text = File.ReadAllText(WorkflowPath());

        text.Should().Contain("/openai/v1/conversations?limit=100&order=asc").And.Contain("/openai/v1/responses?limit=100&order=asc");
        text.Should().NotContain("after=", "a delete of the item a cursor points at would break the cursor, so every request is for the first page");
        text.Should().Contain("lessOrEquals").And.Contain("created_at");
    }

    [Fact]
    public void Parameters_AreAppSettingsTheBicepSets()
    {
        var parameters = JsonNode.Parse(File.ReadAllText(Path.Combine(Root, "src", "Integration", "ConversationSweeperLogicApp", "parameters.json")))!.AsObject();
        var bicep = File.ReadAllText(Path.Combine(Root, Module));

        foreach (var parameter in parameters)
        {
            var setting = parameter.Value!["value"]!.GetValue<string>().Replace("@appsetting('", "").Replace("')", "");
            bicep.Should().Contain($"name: '{setting}'");
        }
    }

    private static readonly string[] Defaults =
        ["param location = 'swedencentral'", "param tags = {}", "param integrationRetentionDays = 90", "param applicationInsightsName = 'appi'", "param planName = 'plan'",
            "param contentKeyVaultName = 'kv'", "param integrationConsolePrincipalId = '00000000-0000-0000-0000-000000000009'", "param foundryAccountName = 'ais'",
            "param foundryProjectName = 'norvia'", "param foundryProjectEndpoint = 'https://ais.services.ai.azure.com/api/projects/norvia'", "param enabled = true"];

    private static List<JsonNode> Resources(params string[] parameters) => BicepSnapshot.Resources(Module,
        [.. Defaults.Where(line => !parameters.Any(given => given.Split(' ')[1] == line.Split(' ')[1])), .. parameters]);

    private static string WorkflowPath() => Path.Combine(Root, "src", "Integration", "ConversationSweeperLogicApp", "conversation-sweeper", "workflow.json");

    private static JsonNode Workflow() => JsonNode.Parse(File.ReadAllText(WorkflowPath()))!;

    private static IEnumerable<JsonNode> Descendants(JsonNode node)
    {
        switch (node)
        {
            case JsonObject obj:
                yield return obj;
                foreach (var child in obj.Select(property => property.Value).OfType<JsonNode>().SelectMany(Descendants)) yield return child;
                break;
            case JsonArray array:
                foreach (var child in array.OfType<JsonNode>().SelectMany(Descendants)) yield return child;
                break;
        }
    }

    private static string Type(JsonNode resource) => resource["type"]!.GetValue<string>();

    private static JsonNode Single(List<JsonNode> resources, string type) => resources.Single(resource => Type(resource) == type);
}
