using NetArchTest.Rules;
using Norvia.Agents.Definitions;
using Norvia.Agents.Tools;
using Norvia.Erp.Api.Agents;
using Norvia.Erp.Application.Agents;
using Norvia.Erp.Application.Agents.RejectionReason;

namespace Norvia.Erp.ArchitectureTests;

/// <summary>
/// The Carrier invoice rejection reason agent (#473, ADR 0040): the boundary is a tool, not an instruction. The reason leaves Norvia in the
/// invoice outcome (E2), so the Agent is a separate definition and identity with exactly one tool, behind a role of its own, and that
/// role cannot reach the other agent-view tools: the Planner's Spot remark, the Rate Card version and other Carriers' references never reach it.
/// </summary>
public sealed class FinanceRejectionReasonAgentRulesTests
{
    private static AgentDefinition Definition(string name) =>
        AgentDefinitionLoader.Parse(File.ReadAllText(Path.Combine(RepositoryRoot.Path, "agents", $"{name}.agent.yaml")));

    private static ToolListContract Contract(string server) =>
        ToolListContract.Parse(File.ReadAllText(Path.Combine(RepositoryRoot.Path, "contracts", "agent-mcp", $"{server}.tools.json")));

    [Fact]
    public void TheAgent_HasExactlyOneTool_AReadOfTheCarrierVisibleFacts()
    {
        var definition = Definition("finance-rejection-reason");
        var contract = Contract(definition.Tools.Server);

        definition.Tools.Names.Should().Equal("get_carrier_visible_line_facts");
        contract.Tools.Should().ContainSingle().Which.Path.Should().EndWith("/carrier-visible-facts");
        contract.Tools.Should().OnlyContain(tool => tool.IsRead, "an Agent has no write tool (ADR 0040)");
        contract.Unknown(definition).Should().BeEmpty();
        definition.Model.MaxToolCalls.Should().Be(1);
    }

    [Fact]
    public void TheAgentsRole_CannotReachTheOtherAgentViewTools()
    {
        var reason = Contract("finance-rejection-reason");
        var matching = Contract("finance-matching");

        reason.Role.Should().Be(AgentRoles.FinanceRejectionReasonRead).And.NotBe(matching.Role);
        matching.Role.Should().Be(AgentRoles.FinanceRead);
        reason.Server.Should().NotBe(matching.Server, "each Agent has an MCP server and an audience of its own");
        reason.Tools.Select(tool => tool.Name).Should().NotIntersectWith(matching.Tools.Select(tool => tool.Name));
        Definition("finance-matching").Tools.Names.Should().NotContain("get_carrier_visible_line_facts", "the matching agent does not read the Carrier-visible facts");
    }

    [Fact]
    public void EachAgentView_RequiresOnlyItsOwnRole()
    {
        var matching = File.ReadAllText(Path.Combine(RepositoryRoot.Path, "src/Erp/Norvia.Erp.Api/Agents/FinanceAgentEndpoints.cs"));
        var reason = File.ReadAllText(Path.Combine(RepositoryRoot.Path, "src/Erp/Norvia.Erp.Api/Agents/FinanceRejectionReasonAgentEndpoints.cs"));

        matching.Should().Contain("RequireRole(AgentRoles.FinanceRead)").And.NotContain("FinanceRejectionReasonRead");
        reason.Should().Contain("RequireRole(AgentRoles.FinanceRejectionReasonRead)").And.NotContain("AgentRoles.FinanceRead)");
        FinanceRejectionReasonAgentEndpoints.Flow.Should().StartWith("agent-view-").And.NotBe(FinanceAgentEndpoints.Flow);
    }

    [Fact]
    public void TheAgentsView_KnowsNoRateCardNoCarrierCostSourceAndNoMatchingTolerance()
    {
        // The Carrier-visible facts are built from the Carrier invoice, the agreed Carrier cost's total and the Carrier's own Open costs. The
        // Rate Card, the cost's source (the Planner's Spot remark) and the matching tolerance are not part of this view.
        var result = Types.InAssembly(typeof(GetCarrierVisibleLineFactsHandler).Assembly)
            .That().ResideInNamespace("Norvia.Erp.Application.Agents.RejectionReason")
            .ShouldNot().HaveDependencyOnAny("Norvia.Erp.Domain.RateCards", "Norvia.Erp.Application.CarrierInvoices.AgentView")
            .GetResult();

        result.IsSuccessful.Should().BeTrue(string.Join(", ", result.FailingTypeNames ?? []));
        typeof(GetCarrierVisibleLineFactsHandler).GetConstructors().Single().GetParameters().Select(parameter => parameter.ParameterType.Name)
            .Should().NotContain("IMatchingTolerance");
        File.ReadAllText(Path.Combine(RepositoryRoot.Path, "src/Erp/Norvia.Erp.Application/Agents/RejectionReason/GetCarrierVisibleLineFactsHandler.cs"))
            .Should().NotContain("Source").And.NotContain("Tolerance");
    }
}
