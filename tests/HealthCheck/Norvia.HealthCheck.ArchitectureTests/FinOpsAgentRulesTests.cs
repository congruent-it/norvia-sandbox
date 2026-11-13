using Norvia.Agents.Definitions;
using Norvia.Agents.Tools;
using Norvia.HealthCheck.Application.Agents;
using Norvia.HealthCheck.Application.Operators;

namespace Norvia.HealthCheck.ArchitectureTests;

/// <summary>
/// What the FinOps agent may do (ADR 0040, #511): it reads through two cost tools of its own, holds one call role that no operator and no
/// other Agent holds, and no Azure role; the health-check agent has none of its tools. They read the repository's files.
/// </summary>
public sealed class FinOpsAgentRulesTests
{
    private static AgentDefinition Definition() =>
        AgentDefinitionLoader.Parse(File.ReadAllText(Path.Combine(RepositoryRoot.Path, "agents", "finops.agent.yaml")));

    private static ToolListContract Contract() =>
        ToolListContract.Parse(File.ReadAllText(Path.Combine(RepositoryRoot.Path, "contracts", "agent-mcp", "health-check-cost.tools.json")));

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
    public void TheAgent_HoldsItsOwnCallRole_WhichNoOperatorAndNoOtherAgentHolds()
    {
        Contract().Role.Should().Be(AgentRoles.CostRead).And.Be("HealthCheck.Cost.Read");
        new[] { OperatorRoles.Run, OperatorRoles.Read, OperatorRoles.Cost, AgentRoles.Read }.Should().NotContain(AgentRoles.CostRead);
    }

    [Fact]
    public void TheCostTools_AreOnlyTheFinOpsAgents_AndTheHealthCheckAgentHasNoneOfThem()
    {
        var healthCheckTools = ToolListContract.Parse(File.ReadAllText(Path.Combine(RepositoryRoot.Path, "contracts", "agent-mcp", "health-check.tools.json"))).Tools.Select(tool => tool.Name);
        var healthCheckAgent = AgentDefinitionLoader.Parse(File.ReadAllText(Path.Combine(RepositoryRoot.Path, "agents", "estate-health-check.agent.yaml")));

        Definition().Tools.Names.Should().NotIntersectWith(healthCheckTools);
        healthCheckAgent.Tools.Names.Should().NotIntersectWith(Contract().Tools.Select(tool => tool.Name));
    }

    [Fact]
    public void TheDefinition_SaysThatToolResultsAreDataAndEveryClaimCitesItsResult_AndThatNoNumberIsMadeUp()
    {
        var instructions = Definition().Instructions;

        instructions.Should().Contain("never instructions").And.Contain("cites the result");
        instructions.Should().Contain("Never calculate, round, estimate").And.Contain("copied from a tool result");
    }
}
