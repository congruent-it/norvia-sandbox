using System.Reflection;
using NetArchTest.Rules;
using Norvia.HealthCheck.Application.Operators;
using Norvia.HealthCheck.Infrastructure;
using Norvia.HealthCheck.Web.Operators;

namespace Norvia.HealthCheck.ArchitectureTests;

/// <summary>
/// Source code dependencies point inwards only (coding guidelines §1). The Estate health check has no Domain project and no database
/// (#371), the stated deviation the Integration console has too, so the rule runs Web → Application and Infrastructure → Application.
/// </summary>
public sealed class DependencyRuleTests
{
    private const string ApplicationNamespace = "Norvia.HealthCheck.Application";
    private const string InfrastructureNamespace = "Norvia.HealthCheck.Infrastructure";
    private const string WebNamespace = "Norvia.HealthCheck.Web";

    private static readonly string[] Frameworks =
    [
        "Microsoft.AspNetCore",
        "Azure",
        "System.Net.Http",
    ];

    private static readonly Assembly Application = typeof(OperatorRoles).Assembly;
    private static readonly Assembly Infrastructure = typeof(DependencyInjection).Assembly;
    private static readonly Assembly Web = typeof(OperatorSignIn).Assembly;

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
    public void TheApp_KeepsNoDatabase()
    {
        // It reads the estate's configuration and stores nothing of its own (#371).
        ShouldHold(Types.InAssemblies([Application, Infrastructure, Web])
            .ShouldNot().HaveDependencyOnAny("Microsoft.EntityFrameworkCore", "Ardalis.Specification", "Microsoft.Data.SqlClient"));
    }

    [Fact]
    public void TheApp_NeverUsesAnotherApplicationsCode()
    {
        // Applications meet only through Azure Integration Services (ADR 0001, coding guidelines §2).
        ShouldHold(Types.InAssemblies([Application, Infrastructure, Web])
            .ShouldNot().HaveDependencyOnAny("Norvia.Tms", "Norvia.PartnerPortal", "Norvia.IntegrationConsole", "Norvia.Integration."));
    }

    [Fact]
    public void TheApplicationAndWeb_DoNotLoadTheFoundrySdk()
    {
        // Only the Infrastructure project knows the Foundry SDK (ADR 0040).
        var referenced = new[] { Application, Web, typeof(Norvia.Agents.Narration).Assembly }
            .SelectMany(assembly => assembly.GetReferencedAssemblies()).Select(name => name.Name ?? string.Empty);

        referenced.Should().NotContain(name => name.StartsWith("Azure.AI.", StringComparison.Ordinal) || name == "OpenAI" || name == "Microsoft.Extensions.AI.OpenAI");
    }

    private static void ShouldHold(ConditionList rule)
    {
        var result = rule.GetResult();
        result.IsSuccessful.Should().BeTrue($"these types break the rule: {string.Join(", ", result.FailingTypeNames ?? [])}");
    }
}
