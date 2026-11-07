using System.Security.Claims;
using Ardalis.Result;
using Mediator;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Norvia.Erp.Api.Agents;
using Norvia.Erp.Application.Agents;
using Norvia.Erp.Application.Agents.RejectionReason;
using Norvia.Erp.Domain.Carriers;

namespace Norvia.Erp.Api.UnitTests;

/// <summary>The rejection reason agent's view of the ERP API (ADR 0040, #473): one read, behind a role of its own that the Finance agent does not hold.</summary>
public sealed class FinanceRejectionReasonAgentViewTests
{
    private static List<RouteEndpoint> Endpoints(Action<IEndpointRouteBuilder> map)
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddAuthorization();
        builder.Services.AddApiVersioning();
        builder.Services.AddSingleton(Substitute.For<IMediator>());
        var app = builder.Build();
        map(app);
        return [.. ((IEndpointRouteBuilder)app).DataSources.SelectMany(source => source.Endpoints).OfType<RouteEndpoint>()];
    }

    private static List<RouteEndpoint> Endpoints() => Endpoints(app => app.MapFinanceRejectionReasonAgent());

    [Fact]
    public void TheView_HasOneRoute_AGet_UnderItsOwnAgentPath()
    {
        var endpoints = Endpoints();

        endpoints.Should().ContainSingle().Which.RoutePattern.RawText.Should().StartWith("/v{version:apiVersion}/agent/finance-rejection-reason/").And.EndWith("/carrier-visible-facts");
        endpoints.SelectMany(endpoint => endpoint.Metadata.OfType<HttpMethodMetadata>()).SelectMany(methods => methods.HttpMethods).Should().Equal("GET");
    }

    [Fact]
    public void TheView_RequiresOnlyItsOwnRole_AndTheFinanceAgentViewRequiresOnlyTheOther()
    {
        static List<string> Roles(RouteEndpoint endpoint) =>
            [.. endpoint.Metadata.OfType<AuthorizationPolicy>().SelectMany(policy => policy.Requirements).OfType<Microsoft.AspNetCore.Authorization.Infrastructure.RolesAuthorizationRequirement>().SelectMany(requirement => requirement.AllowedRoles)];

        foreach (var endpoint in Endpoints())
        {
            Roles(endpoint).Should().Equal(AgentRoles.FinanceRejectionReasonRead);
        }

        foreach (var endpoint in Endpoints(app => app.MapFinanceAgent()))
        {
            Roles(endpoint).Should().Equal(AgentRoles.FinanceRead);
        }

        AgentRoles.FinanceRejectionReasonRead.Should().Be("CarrierInvoices.FinanceRejectionReasonAgent.Read").And.NotBe(AgentRoles.FinanceRead);
        FinanceRejectionReasonAgentEndpoints.Flow.Should().Be("agent-view-finance-rejection-reason").And.NotBe(FinanceAgentEndpoints.Flow);
    }

    [Theory]
    [InlineData("CarrierInvoices.FinanceRejectionReasonAgent.Read", true)]
    [InlineData("CarrierInvoices.FinanceAgent.Read", false)]
    [InlineData("Finance", false)]
    [InlineData("CarrierInvoices.Manage", false)]
    [InlineData(null, false)]
    public async Task TheView_AdmitsOnlyACallerHoldingItsOwnRole(string? role, bool admitted)
    {
        var services = new ServiceCollection().AddLogging().AddAuthorization().BuildServiceProvider();
        var authorization = services.GetRequiredService<IAuthorizationService>();
        var claims = role is null ? [] : new[] { new Claim("roles", role) };
        var caller = new ClaimsPrincipal(new ClaimsIdentity(claims, "test", "name", "roles"));

        var policy = Endpoints().Single().Metadata.OfType<AuthorizationPolicy>().Single();

        (await authorization.AuthorizeAsync(caller, policy)).Succeeded.Should().Be(admitted);
    }

    [Fact]
    public async Task TheFinanceAgentView_DoesNotAdmitTheRejectionReasonAgentsRole()
    {
        var services = new ServiceCollection().AddLogging().AddAuthorization().BuildServiceProvider();
        var caller = new ClaimsPrincipal(new ClaimsIdentity([new Claim("roles", AgentRoles.FinanceRejectionReasonRead)], "test", "name", "roles"));

        foreach (var endpoint in Endpoints(app => app.MapFinanceAgent()))
        {
            var policy = endpoint.Metadata.OfType<AuthorizationPolicy>().Single();

            (await services.GetRequiredService<IAuthorizationService>().AuthorizeAsync(caller, policy)).Succeeded.Should().BeFalse();
        }
    }

    [Fact]
    public async Task Get_AFoundLine_IsTheCarrierVisibleFacts()
    {
        var mediator = Substitute.For<IMediator>();
        var view = new CarrierVisibleLineFactsView("CAR-0001", "VDT-1", "TRX 0042", "UnknownBooking", 80m, "EUR", null, "TRX-0042", null);
#pragma warning disable CA2012 // NSubstitute configures the call by reading its return value, which is never consumed.
        mediator.Send(new GetCarrierVisibleLineFactsQuery(new CarrierId("CAR-0001"), "VDT-1", "TRX 0042"), Arg.Any<CancellationToken>()).Returns(ValueTask.FromResult(Result<CarrierVisibleLineFactsView>.Success(view)));
#pragma warning restore CA2012

        var result = await FinanceRejectionReasonAgentEndpoints.GetCarrierVisibleLineFactsAsync("CAR-0001", "VDT-1", "TRX 0042", mediator, CancellationToken.None);

        result.Result.Should().BeOfType<Ok<CarrierVisibleLineFactsView>>().Which.Value.Should().Be(view);
    }

    [Fact]
    public async Task Get_ALineItDoesNotFind_IsANotFoundProblem()
    {
        var mediator = Substitute.For<IMediator>();
#pragma warning disable CA2012 // NSubstitute configures the call by reading its return value, which is never consumed.
        mediator.Send(Arg.Any<GetCarrierVisibleLineFactsQuery>(), Arg.Any<CancellationToken>()).Returns(ValueTask.FromResult(Result<CarrierVisibleLineFactsView>.NotFound()));
#pragma warning restore CA2012

        var result = await FinanceRejectionReasonAgentEndpoints.GetCarrierVisibleLineFactsAsync("CAR-0001", "VDT-1", "NOPE", mediator, CancellationToken.None);

        result.Result.Should().BeOfType<ProblemHttpResult>().Which.StatusCode.Should().Be(404);
    }
}
