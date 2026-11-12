using System.Reflection;
using NetArchTest.Rules;
using Norvia.PartnerPortal.Application.Bookings.Submit;
using Norvia.PartnerPortal.Domain.Bookings;
using Norvia.PartnerPortal.Infrastructure;
using Norvia.PartnerPortal.Web.Bookings;

namespace Norvia.PartnerPortal.ArchitectureTests;

/// <summary>Source code dependencies point inwards only (coding guidelines §1), and the portal knows Norvia only through the Partner API.</summary>
public sealed class DependencyRuleTests
{
    private const string DomainNamespace = "Norvia.PartnerPortal.Domain";
    private const string ApplicationNamespace = "Norvia.PartnerPortal.Application";
    private const string InfrastructureNamespace = "Norvia.PartnerPortal.Infrastructure";
    private const string WebNamespace = "Norvia.PartnerPortal.Web";

    private static readonly string[] Frameworks =
    [
        "Microsoft.EntityFrameworkCore",
        "Microsoft.AspNetCore",
        "Azure",
        "System.Net.Http",
    ];

    private static readonly Assembly Domain = typeof(Booking).Assembly;
    private static readonly Assembly Application = typeof(SubmitBookingCommand).Assembly;
    private static readonly Assembly Infrastructure = typeof(DependencyInjection).Assembly;
    private static readonly Assembly Web = typeof(IPartnerPortal).Assembly;

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
    public void ThePortal_NeverUsesAnotherApplicationsCode()
    {
        // Applications meet only through Azure Integration Services (ADR 0001, coding guidelines §2).
        ShouldHold(Types.InAssemblies([Domain, Application, Infrastructure, Web])
            .ShouldNot().HaveDependencyOnAny("Norvia.Tms", "Norvia.Integration"));
    }

    [Fact]
    public void OnlyInfrastructure_ReferencesTheFoundrySdkOrTheAgentsRunner()
    {
        // ADR 0049: the Foundry client is the Partner portal's Infrastructure; the layers around it know the port and nothing about the model.
        ShouldHold(Types.InAssemblies([Domain, Application, Web])
            .That().DoNotHaveName("Program")
            .ShouldNot().HaveDependencyOnAny("Azure.AI", "Azure.Identity", "Norvia.Agents", "Microsoft.Extensions.AI", "OpenAI"));
    }

    [Fact]
    public void TheAssistantsPort_IsOwnedByTheApplicationLayer()
    {
        typeof(Norvia.PartnerPortal.Application.TrackingAssistant.IAgentClient).Assembly.Should().BeSameAs(Application);
        ShouldHold(Types.InAssembly(Infrastructure).That().ImplementInterface(typeof(Norvia.PartnerPortal.Application.TrackingAssistant.IAgentClient)).Should().ResideInNamespace("Norvia.PartnerPortal.Infrastructure.Assistant"));
    }

    private static void ShouldHold(ConditionList rule)
    {
        var result = rule.GetResult();
        result.IsSuccessful.Should().BeTrue($"these types break the rule: {string.Join(", ", result.FailingTypeNames ?? [])}");
    }
}
