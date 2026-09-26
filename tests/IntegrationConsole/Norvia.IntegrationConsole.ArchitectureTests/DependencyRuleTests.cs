using System.Reflection;
using NetArchTest.Rules;
using Norvia.IntegrationConsole.Application.Operators;
using Norvia.IntegrationConsole.Infrastructure;
using Norvia.IntegrationConsole.Web.Operators;

namespace Norvia.IntegrationConsole.ArchitectureTests;

/// <summary>
/// Source code dependencies point inwards only (coding guidelines §1). The console has no Domain project and no
/// database, the stated deviation in docs/operations/integration-console.md, so the rule runs Web → Application and
/// Infrastructure → Application.
/// </summary>
public sealed class DependencyRuleTests
{
    private const string ApplicationNamespace = "Norvia.IntegrationConsole.Application";
    private const string InfrastructureNamespace = "Norvia.IntegrationConsole.Infrastructure";
    private const string WebNamespace = "Norvia.IntegrationConsole.Web";

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
    public void TheConsole_KeepsNoDatabase()
    {
        // It reads what the estate records and stores nothing of its own (ADR 0017).
        ShouldHold(Types.InAssemblies([Application, Infrastructure, Web])
            .ShouldNot().HaveDependencyOnAny("Microsoft.EntityFrameworkCore", "Ardalis.Specification", "Microsoft.Data.SqlClient"));
    }

    [Fact]
    public void TheConsole_NeverUsesAnotherApplicationsCode()
    {
        // Applications meet only through Azure Integration Services (ADR 0001, coding guidelines §2).
        ShouldHold(Types.InAssemblies([Application, Infrastructure, Web])
            .ShouldNot().HaveDependencyOnAny("Norvia.Tms", "Norvia.PartnerPortal", "Norvia.Integration."));
    }

    private static void ShouldHold(ConditionList rule)
    {
        var result = rule.GetResult();
        result.IsSuccessful.Should().BeTrue($"these types break the rule: {string.Join(", ", result.FailingTypeNames ?? [])}");
    }
}
