using System.Reflection;
using NetArchTest.Rules;
using Norvia.Tms.Api.TransportOrders;
using Norvia.Tms.Application.TransportOrders.Submit;
using Norvia.Tms.Domain.TransportOrders;
using Norvia.Tms.Infrastructure;
using Norvia.Tms.Web.TransportOrders;

namespace Norvia.Tms.ArchitectureTests;

/// <summary>Source code dependencies point inwards only (coding guidelines §1).</summary>
public sealed class DependencyRuleTests
{
    private const string DomainNamespace = "Norvia.Tms.Domain";
    private const string ApplicationNamespace = "Norvia.Tms.Application";
    private const string InfrastructureNamespace = "Norvia.Tms.Infrastructure";
    private const string ApiNamespace = "Norvia.Tms.Api";
    private const string WebNamespace = "Norvia.Tms.Web";

    private static readonly string[] Frameworks =
    [
        "Microsoft.EntityFrameworkCore",
        "Microsoft.AspNetCore",
        "Azure",
        "System.Net.Http",
    ];

    private static readonly Assembly Domain = typeof(TransportOrder).Assembly;
    private static readonly Assembly Application = typeof(SubmitTransportOrderCommand).Assembly;
    private static readonly Assembly Infrastructure = typeof(DependencyInjection).Assembly;
    private static readonly Assembly Api = typeof(TransportOrderEndpoints).Assembly;
    private static readonly Assembly Web = typeof(ITransportOrderQueries).Assembly;

    [Fact]
    public void Domain_DependsOnNoOuterLayer()
    {
        ShouldHold(Types.InAssembly(Domain).ShouldNot().HaveDependencyOnAny(ApplicationNamespace, InfrastructureNamespace, ApiNamespace, WebNamespace));
    }

    [Fact]
    public void Domain_DependsOnNoFramework()
    {
        ShouldHold(Types.InAssembly(Domain).ShouldNot().HaveDependencyOnAny(Frameworks));
    }

    [Fact]
    public void Application_DependsOnNoOuterLayer()
    {
        ShouldHold(Types.InAssembly(Application).ShouldNot().HaveDependencyOnAny(InfrastructureNamespace, ApiNamespace, WebNamespace));
    }

    [Fact]
    public void Application_DependsOnNoFramework()
    {
        ShouldHold(Types.InAssembly(Application).ShouldNot().HaveDependencyOnAny(Frameworks));
    }

    [Fact]
    public void Infrastructure_DoesNotDependOnPresentation()
    {
        ShouldHold(Types.InAssembly(Infrastructure).ShouldNot().HaveDependencyOnAny(ApiNamespace, WebNamespace));
    }

    [Fact]
    public void Api_UsesInfrastructureOnlyInItsCompositionRoot()
    {
        ShouldHold(Types.InAssembly(Api)
            .That().DoNotHaveName("Program")
            .And().DoNotHaveNameStartingWith("<")
            .ShouldNot().HaveDependencyOn(InfrastructureNamespace));
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
    public void Web_OnlyReadsThroughTheQuerySide()
    {
        // The source-generated mediator knows every handler; the UI's own types must never send a command.
        ShouldHold(Types.InAssembly(Web)
            .That().DoNotResideInNamespace("Mediator")
            .ShouldNot().HaveDependencyOn("Norvia.Tms.Application.TransportOrders.Submit"));
    }

    [Fact]
    public void DbContext_StaysInsideInfrastructure()
    {
        ShouldHold(Types.InAssemblies([Domain, Application, Api, Web])
            .That().DoNotHaveName("Program")
            .ShouldNot().HaveDependencyOn("Microsoft.EntityFrameworkCore"));
    }

    private static void ShouldHold(ConditionList rule)
    {
        var result = rule.GetResult();
        result.IsSuccessful.Should().BeTrue($"these types break the rule: {string.Join(", ", result.FailingTypeNames ?? [])}");
    }
}
