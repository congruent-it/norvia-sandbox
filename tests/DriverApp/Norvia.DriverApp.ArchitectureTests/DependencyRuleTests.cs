using System.Reflection;
using NetArchTest.Rules;
using Norvia.DriverApp.Application.Trips.Receive;
using Norvia.DriverApp.Domain.Trips;
using Norvia.DriverApp.Infrastructure;
using Norvia.DriverApp.Web.Drivers;

namespace Norvia.DriverApp.ArchitectureTests;

/// <summary>Source code dependencies point inwards only (coding guidelines §1), and the Driver app never calls the TMS (ADR 0001).</summary>
public sealed class DependencyRuleTests
{
    private const string DomainNamespace = "Norvia.DriverApp.Domain";
    private const string ApplicationNamespace = "Norvia.DriverApp.Application";
    private const string InfrastructureNamespace = "Norvia.DriverApp.Infrastructure";
    private const string ApiNamespace = "Norvia.DriverApp.Api";
    private const string WebNamespace = "Norvia.DriverApp.Web";

    private static readonly string[] Frameworks =
    [
        "Microsoft.EntityFrameworkCore",
        "Microsoft.AspNetCore",
        "Azure",
        "System.Net.Http",
    ];

    private static readonly Assembly Domain = typeof(DriverTrip).Assembly;
    private static readonly Assembly Application = typeof(ReceiveTripVersionCommand).Assembly;
    private static readonly Assembly Infrastructure = typeof(DependencyInjection).Assembly;
    private static readonly Assembly Api = typeof(Api.Trips.TripEndpoints).Assembly;
    private static readonly Assembly Web = typeof(DriverSignIn).Assembly;

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
    public void Presentation_UsesTheApplicationOnly_NotTheDomain()
    {
        ShouldHold(Types.InAssemblies([Api, Web]).ShouldNot().HaveDependencyOn(DomainNamespace));
    }

    [Fact]
    public void DbContext_StaysInsideInfrastructure()
    {
        ShouldHold(Types.InAssemblies([Domain, Application, Api, Web])
            .That().DoNotHaveName("Program")
            .ShouldNot().HaveDependencyOn("Microsoft.EntityFrameworkCore"));
    }

    [Fact]
    public void TheDriverApp_NeverReferencesTheTms()
    {
        foreach (var assembly in new[] { Domain, Application, Infrastructure, Api, Web })
        {
            ShouldHold(Types.InAssembly(assembly).ShouldNot().HaveDependencyOn("Norvia.Tms"));
        }
    }

    [Fact]
    public void TheDriverApp_MakesNoHttpCallsAtAll()
    {
        // Nothing flows back from the Driver app in P11 except the Logic App's delivery report, which the Logic App makes itself.
        foreach (var assembly in new[] { Domain, Application, Infrastructure, Api, Web })
        {
            ShouldHold(Types.InAssembly(assembly).ShouldNot().HaveDependencyOnAny("System.Net.Http.HttpClient", "System.Net.Http.IHttpClientFactory"));
        }
    }

    private static void ShouldHold(ConditionList conditions)
    {
        var result = conditions.GetResult();
        result.IsSuccessful.Should().BeTrue("these types break the rule: {0}", string.Join(", ", result.FailingTypeNames ?? []));
    }
}
