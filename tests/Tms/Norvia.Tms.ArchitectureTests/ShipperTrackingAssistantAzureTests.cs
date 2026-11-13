using System.Text.RegularExpressions;

namespace Norvia.Tms.ArchitectureTests;

/// <summary>
/// The Shipper tracking assistant in Azure (#879, ADR 0049): its identity holds no role and its definition no tool, it is published into the Intake
/// project, the portal alone may ask it, and the estate's limits are parameters. Nothing is deployed; the Bicep and the scripts are read.
/// </summary>
public sealed class ShipperTrackingAssistantAzureTests
{
    private const string Agent = "shipper-tracking-assistant";

    private static string Read(string path) => File.ReadAllText(Path.Combine(RepositoryRoot.Path, path));

    [Fact]
    public void TheDefinition_ListsNoTool_AndNamesNoServer()
    {
        var definition = Read($"agents/{Agent}.agent.yaml");

        definition.Should().NotMatchRegex(@"(?m)^tools:", "ADR 0049: the tool list is empty, so the section is left out");
        definition.Should().NotContain("server:");
    }

    [Fact]
    public void TheAssistantsIdentity_IsGivenNoRole_BecauseNothingMapsItToAToolServer()
    {
        // assign-agent-roles.ps1 gives a role to each entry deploy-agents.ps1 writes, and deploy-agents.ps1 writes one only for an Agent with a tool server.
        Read("infra/deploy-agents.ps1").Should().Contain("An Agent with no tool reads nothing, so its identity needs no role.").And.Contain("if (-not $server) { continue }");
        Read("infra/assign-agent-roles.ps1").Should().NotContain(Agent);
        Directory.GetFiles(Path.Combine(RepositoryRoot.Path, "contracts", "agent-mcp"), "*.tools.json")
            .Should().OnlyContain(file => !File.ReadAllText(file).Contains(Agent, StringComparison.Ordinal), "no tool server is the assistant's");
        Regex.IsMatch(Read("infra/estate.bicep"), Agent).Should().BeFalse("the estate gives the assistant's identity nothing: Foundry makes it on publish");
    }

    [Fact]
    public void TheAssistant_IsPublishedIntoTheIntakeProject_WhereAShippersQuestionIsNotTraced()
    {
        Read("infra/deploy-agents.ps1").Should().Contain("$intakeAgents = @('booking-intake', 'shipper-tracking-assistant')");
    }

    [Fact]
    public void ThePartnerPortalsIdentity_IsGivenFoundryUserOnTheIntakeProjectOnly()
    {
        var estate = Read("infra/estate.bicep");
        var applications = estate[estate.IndexOf("applicationPrincipalIds:", StringComparison.Ordinal)..];
        applications = applications[..applications.IndexOf('\n', StringComparison.Ordinal)];

        estate.Should().Contain("intakeApplicationPrincipalIds: [bookingDraft.outputs.principalId, identities.outputs.partnerPortalPrincipalId]");
        applications.Should().NotContain("partnerPortal", "the norvia project is never the portal's");
    }

    [Fact]
    public void TheAssistantsModelCost_IsTaggedOnTheFoundryResources_AndTheModelIsTheSharedDataZoneStandardDeployment()
    {
        var foundry = Read("infra/estate/foundry.bicep");

        foundry.Should().Contain("'cost-category': 'ai-model'").And.Contain("name: 'DataZoneStandard'");
        Read("agents/shipper-tracking-assistant.agent.yaml").Should().Contain("deployment: gpt-5.4-mini");
    }

    [Fact]
    public void AgentsEnabled_IsOffUnlessTheEnvironmentSaysSo_SoThePanelIsHiddenByDefault()
    {
        Read("infra/provision-estate.ps1").Should().Contain("$agentsEnabled = (\"$env:ESTATE_AGENTS_ENABLED_SETTING\".ToLowerInvariant() -eq 'true') ? 'true' : 'false'");
        Read("src/Norvia.AppHost/AppHost.cs").Should().Contain(".WithEnvironment(\"Agents__Enabled\", estate.AgentsEnabled)");
    }

    [Fact]
    public void TheLimits_AreEstateParameters_WithTheAdrsNumbersAsDefaults()
    {
        var script = Read("infra/provision-estate.ps1");

        script.Should().MatchRegex(@"questions_per_minute\s+= 6").And.MatchRegex(@"questions_per_hour\s+= 60").And.MatchRegex(@"max_question_characters\s+= 500")
            .And.MatchRegex(@"max_orders\s+= 20").And.MatchRegex(@"max_events_per_order\s+= 20").And.MatchRegex(@"max_output_tokens\s+= 400")
            .And.MatchRegex(@"daily_tokens_per_persona\s+= 50000").And.MatchRegex(@"daily_tokens_estate\s+= 500000");
        script.Should().Contain("Set-AzdParameter \"estate_assistant_$limit\" $value");
    }

    [Fact]
    public void SmokeRun_AsksAsAPersona_ChecksTheAnswersValuesAndTheOtherPersonasOrder_AndTheIdentitysRoles()
    {
        var script = Read("infra/smoke-test-tracking-assistant.ps1");

        script.Should().Contain("hold no role").And.Contain("Foundry User").And.Contain("norvia-intake");
        script.Should().Contain("not reachable").And.Contain("every value");
        script.Split('\n').Where(line => line.TrimStart().StartsWith("Write-", StringComparison.Ordinal))
            .Should().NotContain(line => line.Contains("$answer", StringComparison.Ordinal) || line.Contains("$token", StringComparison.Ordinal) || line.Contains("Key", StringComparison.Ordinal));
    }
}
