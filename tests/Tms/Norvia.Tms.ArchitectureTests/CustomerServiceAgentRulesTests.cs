using NetArchTest.Rules;
using Norvia.Agents.Definitions;
using Norvia.Agents.Tools;
using Norvia.Tms.Api.Agents;
using Norvia.Tms.Application.Agents;
using Norvia.Tms.Infrastructure.Agents;
using Norvia.Tms.Web.Agents;

namespace Norvia.Tms.ArchitectureTests;

/// <summary>
/// What the Customer service agent may do in the TMS (ADR 0040, ADR 0028, #421): the TMS UI reads in process while the Agent's tools go
/// through the TMS API; the Agent holds one read role, no tool writes, and only Infrastructure knows the Foundry SDK.
/// </summary>
public sealed class CustomerServiceAgentRulesTests
{
    private static readonly string[] FoundrySdk = ["Azure.AI.OpenAI", "Azure.AI.Projects", "Azure.AI.Agents", "Microsoft.Extensions.AI.OpenAI", "OpenAI"];

    private static AgentDefinition Definition() =>
        AgentDefinitionLoader.Parse(File.ReadAllText(Path.Combine(RepositoryRoot.Path, "agents", "customer-service.agent.yaml")));

    private static ToolListContract Contract() =>
        ToolListContract.Parse(File.ReadAllText(Path.Combine(RepositoryRoot.Path, "contracts", "agent-mcp", "customer-service.tools.json")));

    [Fact]
    public void TheAgent_ListsOnlyReadTools_ThatItsServerServes()
    {
        var definition = Definition();
        var contract = Contract();

        definition.Tools.Server.Should().Be(contract.Server);
        contract.Unknown(definition).Should().BeEmpty();
        definition.Tools.Names.Select(name => contract.Find(name)!).Should().OnlyContain(tool => tool.IsRead, "an Agent has no write tool (ADR 0040)");
    }

    [Fact]
    public void TheAgent_HoldsTheOneReadRole_AndEveryToolIsOnTheAgentViewOfTheTmsApi()
    {
        Contract().Role.Should().Be(AgentRoles.CustomerServiceRead);
        typeof(AgentRoles).GetFields().Select(field => field.Name).Should().Equal(
            [nameof(AgentRoles.CustomerServiceRead), nameof(AgentRoles.CustomerServiceReplyRead), nameof(AgentRoles.PlannerRead), nameof(AgentRoles.PlannerNotificationRead)], "each of the four Agents holds one role of its own on the TMS API");
        CustomerServiceAgentEndpoints.Flow.Should().StartWith("agent-view-");
        Contract().Tools.Should().OnlyContain(tool => tool.Path.StartsWith("/rejected-bookings/", StringComparison.Ordinal) || tool.Path.StartsWith("/transport-orders/", StringComparison.Ordinal) || tool.Path.StartsWith("/shipments/", StringComparison.Ordinal));
    }

    [Fact]
    public void OnlyInfrastructure_ImplementsThePortToAnAgent()
    {
        var implementations = Types.InAssemblies([typeof(IAgentClient).Assembly, typeof(AgentsInfrastructure).Assembly, typeof(IExplainRequests).Assembly, typeof(CustomerServiceAgentEndpoints).Assembly])
            .That().ImplementInterface(typeof(IAgentClient)).GetTypes();

        implementations.Should().NotBeEmpty().And.OnlyContain(type => type.Namespace!.StartsWith("Norvia.Tms.Infrastructure", StringComparison.Ordinal));
    }

    [Fact]
    public void TheTmsUi_ReadsInProcess_AndDoesNotCallTheAgentView()
    {
        // The Agent's tools go through the TMS API (an MCP server calls its agent view); the TMS UI never calls that view itself (ADR 0028).
        var result = Types.InAssembly(typeof(IExplainRequests).Assembly).ShouldNot().HaveDependencyOn("Norvia.Tms.Api").GetResult();

        result.IsSuccessful.Should().BeTrue(string.Join(", ", result.FailingTypeNames ?? []));
    }

    [Fact]
    public void OnlyTheTmsInfrastructure_ReferencesTheFoundrySdk()
    {
        foreach (var project in new[] { "Norvia.Tms.Application", "Norvia.Tms.Domain", "Norvia.Tms.Api", "Norvia.Tms.Web" })
        {
            var file = Path.Combine(RepositoryRoot.Path, "src", "Tms", project, $"{project}.csproj");

            File.ReadAllText(file).Should().NotContainAny(FoundrySdk.Select(package => $"Include=\"{package}\""), $"{project} must not reference the Foundry SDK (ADR 0040)");
        }

        File.ReadAllText(Path.Combine(RepositoryRoot.Path, "src/Tms/Norvia.Tms.Infrastructure/Norvia.Tms.Infrastructure.csproj")).Should().Contain("Azure.AI.OpenAI");
    }
}
