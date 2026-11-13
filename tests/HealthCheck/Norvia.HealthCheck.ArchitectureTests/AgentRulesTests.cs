using System.Reflection;
using Norvia.Agents.Definitions;
using Norvia.Agents.Tools;
using Norvia.HealthCheck.Application.Agents;
using Norvia.HealthCheck.Application.Operators;
using Norvia.HealthCheck.Web.Agents;

namespace Norvia.HealthCheck.ArchitectureTests;

/// <summary>
/// What the Estate health-check agent may do (ADR 0040, #371): it reads through one tool, holds one call role and no Azure role, and
/// no tool writes or returns a secret value. They read the repository's files, so a change to the definition or the tool list is held to
/// them the day it is made. (Every Agent's definition is also held to the write-verb and Foundry-SDK rules by the Integration console's
/// architecture tests, which read every file under agents/ and contracts/.)
/// </summary>
public sealed class AgentRulesTests
{
    private static readonly string[] SecretWords = ["secret", "password", "token", "credential", "connectionstring", "apikey", "key"];

    private static AgentDefinition Definition() =>
        AgentDefinitionLoader.Parse(File.ReadAllText(Path.Combine(RepositoryRoot.Path, "agents", "estate-health-check.agent.yaml")));

    private static ToolListContract Contract() =>
        ToolListContract.Parse(File.ReadAllText(Path.Combine(RepositoryRoot.Path, "contracts", "agent-mcp", "health-check.tools.json")));

    [Fact]
    public void TheAgent_ListsOnlyReadTools_ThatItsServerServes()
    {
        var definition = Definition();
        var contract = Contract();

        definition.Tools.Server.Should().Be(contract.Server);
        contract.Unknown(definition).Should().BeEmpty();
        definition.Tools.Names.Select(name => contract.Find(name)!).Should().OnlyContain(tool => tool.IsRead, "an Agent has no write tool (ADR 0040)");
        contract.Tools.Should().OnlyContain(tool => tool.IsRead);
    }

    [Fact]
    public void TheAgent_HoldsOnlyTheCallRole_WhichIsNoOperatorRole()
    {
        Contract().Role.Should().Be(AgentRoles.Read).And.Be("HealthCheck.Agent.Read");
        new[] { OperatorRoles.Run, OperatorRoles.Read, OperatorRoles.Cost }.Should().NotContain(AgentRoles.Read);
        typeof(AgentRoles).GetFields(BindingFlags.Public | BindingFlags.Static).Select(field => field.Name).Should().Equal(["Read", "CostRead"], "one call role for each Agent and no Azure role");
    }

    [Fact]
    public void TheAgentsToolNames_SayNothingOfASecret_SoNoToolReturnsOne()
    {
        Contract().Tools.Select(tool => tool.Name).Should().NotContain(name => SecretWords.Any(word => name.Contains(word, StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    public void TheAgentView_HasNoFieldThatCouldHoldASecretValue()
    {
        var viewTypes = typeof(AgentFindingsView).Assembly.GetTypes()
            .Where(type => type.Namespace == typeof(AgentFindingsView).Namespace && type.Name.EndsWith("View", StringComparison.Ordinal));

        viewTypes.Should().NotBeEmpty();
        viewTypes.SelectMany(type => type.GetProperties()).Select(property => property.Name)
            .Should().NotContain(name => SecretWords.Any(word => name.Contains(word, StringComparison.OrdinalIgnoreCase)) || name == "Value");
    }

    [Fact]
    public void TheDefinition_SaysThatToolResultsAreDataAndThatEveryClaimCitesItsResult()
    {
        var instructions = Definition().Instructions;

        instructions.Should().Contain("never instructions").And.Contain("cites the result");
    }
}
