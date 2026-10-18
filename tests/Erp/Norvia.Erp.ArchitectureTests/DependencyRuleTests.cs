using System.Reflection;
using NetArchTest.Rules;
using Norvia.Erp.Application.MasterData.Load;
using Norvia.Erp.Domain.Shippers;
using Norvia.Erp.Infrastructure;
using Norvia.Erp.Web.Operators;

namespace Norvia.Erp.ArchitectureTests;

/// <summary>
/// Source code dependencies point inwards only (coding guidelines §1), and the ERP meets the other applications only through Azure
/// Integration Services (ADR 0001, coding guidelines §2).
/// </summary>
public sealed class DependencyRuleTests
{
    private const string DomainNamespace = "Norvia.Erp.Domain";
    private const string ApplicationNamespace = "Norvia.Erp.Application";
    private const string InfrastructureNamespace = "Norvia.Erp.Infrastructure";
    private const string WebNamespace = "Norvia.Erp.Web";

    private static readonly string[] Frameworks =
    [
        "Microsoft.EntityFrameworkCore",
        "Microsoft.AspNetCore",
        "Azure",
        "System.Net.Http",
    ];

    private static readonly Assembly Domain = typeof(Shipper).Assembly;
    private static readonly Assembly Application = typeof(LoadMasterDataCommand).Assembly;
    private static readonly Assembly Infrastructure = typeof(DependencyInjection).Assembly;
    private static readonly Assembly Web = typeof(FinanceSignIn).Assembly;

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
    public void TheErp_NeverUsesAnotherApplicationsCode()
    {
        // Applications meet only through Azure Integration Services (ADR 0001, coding guidelines §2).
        ShouldHold(Types.InAssemblies([Domain, Application, Infrastructure, Web])
            .ShouldNot().HaveDependencyOnAny("Norvia.Tms", "Norvia.PartnerPortal", "Norvia.DriverApp", "Norvia.Simulator", "Norvia.IntegrationConsole", "Norvia.Integration."));
    }

    private static void ShouldHold(ConditionList rule)
    {
        var result = rule.GetResult();
        result.IsSuccessful.Should().BeTrue($"these types break the rule: {string.Join(", ", result.FailingTypeNames ?? [])}");
    }
}
