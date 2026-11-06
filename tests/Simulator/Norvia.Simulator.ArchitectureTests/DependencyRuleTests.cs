using System.Reflection;
using NetArchTest.Rules;
using Norvia.Simulator.Application.Runs.Start;
using Norvia.Simulator.Domain.Runs;
using Norvia.Simulator.Infrastructure;
using Norvia.Simulator.Web;

namespace Norvia.Simulator.ArchitectureTests;

/// <summary>
/// Source code dependencies point inwards only (coding guidelines §1), and the Simulator reaches the estate only through the
/// estate's real doors (ADR 0001): it never writes to another database or publishes to Service Bus itself.
/// </summary>
public sealed class DependencyRuleTests
{
    private const string DomainNamespace = "Norvia.Simulator.Domain";
    private const string ApplicationNamespace = "Norvia.Simulator.Application";
    private const string InfrastructureNamespace = "Norvia.Simulator.Infrastructure";
    private const string WebNamespace = "Norvia.Simulator.Web";

    private static readonly string[] Frameworks =
    [
        "Microsoft.EntityFrameworkCore",
        "Microsoft.AspNetCore",
        "Azure",
        "System.Net.Http",
    ];

    private static readonly Assembly Domain = typeof(SimulationRun).Assembly;
    private static readonly Assembly Application = typeof(StartRunCommand).Assembly;
    private static readonly Assembly Infrastructure = typeof(DependencyInjection).Assembly;
    private static readonly Assembly Web = typeof(ISimulatorRequests).Assembly;

    [Fact]
    public void Domain_DependsOnNoOuterLayer()
    {
        ShouldHold(Types.InAssembly(Domain).ShouldNot().HaveDependencyOnAny(ApplicationNamespace, InfrastructureNamespace, WebNamespace));
    }

    [Fact]
    public void Domain_DependsOnNoFramework()
    {
        ShouldHold(Types.InAssembly(Domain).ShouldNot().HaveDependencyOnAny(Frameworks));
    }

    [Fact]
    public void Application_DependsOnNoOuterLayer()
    {
        ShouldHold(Types.InAssembly(Application).ShouldNot().HaveDependencyOnAny(InfrastructureNamespace, WebNamespace));
    }

    [Fact]
    public void Application_DependsOnNoFramework()
    {
        ShouldHold(Types.InAssembly(Application).ShouldNot().HaveDependencyOnAny(Frameworks));
    }

    [Fact]
    public void Infrastructure_DoesNotDependOnPresentation()
    {
        ShouldHold(Types.InAssembly(Infrastructure).ShouldNot().HaveDependencyOn(WebNamespace));
    }

    [Fact]
    public void Web_UsesInfrastructureOnlyInItsCompositionRoot()
    {
        ShouldHold(Types.InAssembly(Web)
            .That().DoNotHaveName("Program")
            .And().DoNotHaveNameStartingWith("<")
            .ShouldNot().HaveDependencyOn(InfrastructureNamespace));
    }

    [Fact]
    public void DbContext_StaysInsideInfrastructure()
    {
        ShouldHold(Types.InAssemblies([Domain, Application, Web])
            .That().DoNotHaveName("Program")
            .ShouldNot().HaveDependencyOn("Microsoft.EntityFrameworkCore"));
    }

    [Fact]
    public void TheSimulator_NeverUsesAnotherApplicationsCode()
    {
        // Applications meet only through Azure Integration Services (ADR 0001, coding guidelines §2).
        ShouldHold(Types.InAssemblies([Domain, Application, Infrastructure, Web])
            .ShouldNot().HaveDependencyOnAny("Norvia.Tms", "Norvia.PartnerPortal", "Norvia.DriverApp", "Norvia.IntegrationConsole", "Norvia.Integration."));
    }

    [Fact]
    public void TheSimulator_NeverPublishesToServiceBusItself()
    {
        // Every generated record has a real Message trail because it comes in through a real door (ADR 0003).
        ShouldHold(Types.InAssemblies([Domain, Application, Infrastructure, Web])
            .ShouldNot().HaveDependencyOnAny("Azure.Messaging.ServiceBus", "Azure.Storage"));
    }

    [Fact]
    public void TheSimulator_ResetsTheEstateOnlyThroughItsDoors_NeverItsDatabasesOrServiceBus()
    {
        // The reset is each application's own door (ADR 0039): the Simulator's client sends HTTP and nothing else.
        ShouldHold(Types.InAssembly(Infrastructure)
            .That().ResideInNamespace("Norvia.Simulator.Infrastructure.Reset")
            .ShouldNot().HaveDependencyOnAny("Microsoft.EntityFrameworkCore", "Microsoft.Data.SqlClient", "Azure.Messaging.ServiceBus", "Azure.Storage"));
    }

    [Fact]
    public void OnlyInfrastructure_KnowsTheAgentsLibraryOrTheModelSdk()
    {
        // The Simulator reaches an Agent through its IAgentClient port; the runner, the scripted fake and any Foundry SDK stay behind it (ADR 0040).
        ShouldHold(Types.InAssemblies([Domain, Application, Web])
            .ShouldNot().HaveDependencyOnAny("Norvia.Agents", "Microsoft.Extensions.AI", "Azure.AI", "OpenAI", "ModelContextProtocol"));
    }

    [Fact]
    public void TheAgentPort_IsDefinedInApplication_AndTheLocalClientImplementsItInInfrastructure()
    {
        typeof(Norvia.Simulator.Application.Ports.IAgentClient).Assembly.Should().BeSameAs(Application);
        ShouldHold(Types.InAssembly(Infrastructure).That().ImplementInterface(typeof(Norvia.Simulator.Application.Ports.IAgentClient)).Should().ResideInNamespace("Norvia.Simulator.Infrastructure.Personas"));
    }

    [Fact]
    public void ThePersonasAgent_HasNoToolsAndNoMcpServer()
    {
        // Pure generation: the definition lists no tool, so the agent can read and write nothing in the estate (ADR 0040).
        var definition = File.ReadAllText(Path.Combine(RepositoryRoot, "agents", "simulator-personas.agent.yaml"));

        definition.Should().NotContain("\ntools:").And.NotContain("server:");
    }

    private static string RepositoryRoot
    {
        get
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Norvia.slnx")))
            {
                directory = directory.Parent;
            }

            return directory?.FullName ?? throw new InvalidOperationException("The repository root isn't above the test assembly.");
        }
    }

    private static void ShouldHold(ConditionList rule)
    {
        var result = rule.GetResult();
        result.IsSuccessful.Should().BeTrue($"these types break the rule: {string.Join(", ", result.FailingTypeNames ?? [])}");
    }
}
