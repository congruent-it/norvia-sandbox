using NetArchTest.Rules;
using Norvia.Agents.Definitions;
using Norvia.Agents.Tools;
using Norvia.Erp.Api.Agents;
using Norvia.Erp.Application.Agents;
using Norvia.Erp.Infrastructure.Agents;
using Norvia.Erp.Web.Agents;

namespace Norvia.Erp.ArchitectureTests;

/// <summary>
/// What the Carrier invoice matching agent may do in the ERP (ADR 0040, #469): the ERP UI reads and writes the ERP's own way, in process,
/// while the Agent's tools go through the ERP API; the Agent holds one read role, no tool writes, and only Infrastructure knows the
/// Foundry SDK.
/// </summary>
public sealed class FinanceAgentRulesTests
{
    private static readonly string[] FoundrySdk = ["Azure.AI.OpenAI", "Azure.AI.Projects", "Azure.AI.Agents", "Microsoft.Extensions.AI.OpenAI", "OpenAI"];

    private static AgentDefinition Definition() =>
        AgentDefinitionLoader.Parse(File.ReadAllText(Path.Combine(RepositoryRoot.Path, "agents", "finance-matching.agent.yaml")));

    private static ToolListContract Contract() =>
        ToolListContract.Parse(File.ReadAllText(Path.Combine(RepositoryRoot.Path, "contracts", "agent-mcp", "finance-matching.tools.json")));

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
    public void TheAgent_HoldsTheOneReadRole_AndEveryToolIsOnTheAgentViewOfTheErpApi()
    {
        Contract().Role.Should().Be(AgentRoles.FinanceRead);
        typeof(AgentRoles).GetFields().Select(field => field.Name).Should().Equal(
            [nameof(AgentRoles.FinanceRead), nameof(AgentRoles.FinanceRejectionReasonRead)], "each of the two Agents holds one role of its own on the ERP API");
        FinanceAgentEndpoints.Flow.Should().StartWith("agent-view-");
        Contract().Tools.Should().OnlyContain(tool => tool.Path.StartsWith("/carriers/", StringComparison.Ordinal));
    }

    [Fact]
    public void OnlyInfrastructure_ImplementsThePortToAnAgent()
    {
        var implementations = Types.InAssemblies([typeof(IAgentClient).Assembly, typeof(AgentsInfrastructure).Assembly, typeof(IExplainRequests).Assembly, typeof(FinanceAgentEndpoints).Assembly])
            .That().ImplementInterface(typeof(IAgentClient)).GetTypes();

        implementations.Should().NotBeEmpty().And.OnlyContain(type => type.Namespace!.StartsWith("Norvia.Erp.Infrastructure", StringComparison.Ordinal));
    }

    [Fact]
    public void TheErpUi_FollowsTheErpsOwnPattern_AndNeverCallsTheAgentViewOfTheApi()
    {
        // The ERP UI reads and decides in process through the Application's mediator. The Agent's tools go through the ERP API (an MCP
        // server calls its agent view); the ERP UI never calls that view itself.
        var result = Types.InAssembly(typeof(IExplainRequests).Assembly).ShouldNot().HaveDependencyOn("Norvia.Erp.Api").GetResult();

        result.IsSuccessful.Should().BeTrue(string.Join(", ", result.FailingTypeNames ?? []));
    }

    [Fact]
    public void TheErpApi_DoesNotAskAnAgent()
    {
        // The API serves the agent view; it never runs the Agent (its port stays the disabled one).
        var result = Types.InAssembly(typeof(FinanceAgentEndpoints).Assembly).That().ResideInNamespace("Norvia.Erp.Api.Agents").ShouldNot().HaveDependencyOn("Norvia.Agents.Runner").GetResult();

        result.IsSuccessful.Should().BeTrue(string.Join(", ", result.FailingTypeNames ?? []));
    }

    [Fact]
    public void OnlyTheErpInfrastructure_ReferencesTheFoundrySdk()
    {
        foreach (var project in new[] { "Norvia.Erp.Application", "Norvia.Erp.Domain", "Norvia.Erp.Api", "Norvia.Erp.Web" })
        {
            var file = Path.Combine(RepositoryRoot.Path, "src", "Erp", project, $"{project}.csproj");

            File.ReadAllText(file).Should().NotContainAny(FoundrySdk.Select(package => $"Include=\"{package}\""), $"{project} must not reference the Foundry SDK (ADR 0040)");
        }

        File.ReadAllText(Path.Combine(RepositoryRoot.Path, "src/Erp/Norvia.Erp.Infrastructure/Norvia.Erp.Infrastructure.csproj")).Should().Contain("Azure.AI.OpenAI");
    }

    [Fact]
    public void NoTypeOutsideInfrastructure_DependsOnTheFoundrySdk()
    {
        var result = Types.InAssemblies([typeof(IAgentClient).Assembly, typeof(IExplainRequests).Assembly, typeof(FinanceAgentEndpoints).Assembly])
            .ShouldNot().HaveDependencyOnAny(FoundrySdk).GetResult();

        result.IsSuccessful.Should().BeTrue(string.Join(", ", result.FailingTypeNames ?? []));
    }
}
