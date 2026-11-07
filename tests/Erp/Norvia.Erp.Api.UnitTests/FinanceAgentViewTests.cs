using System.Security.Claims;
using Ardalis.Result;
using Mediator;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Testing;
using Microsoft.Extensions.Options;
using Norvia.Erp.Api.Agents;
using Norvia.Erp.Application.Agents;
using Norvia.Erp.Application.CarrierInvoices.AgentView;
using Norvia.Erp.Domain.Carriers;

namespace Norvia.Erp.Api.UnitTests;

/// <summary>The Finance agent's view of the ERP API (ADR 0040): behind one read role, read only, and a hop on the Carrier invoice's Message trail.</summary>
public sealed class FinanceAgentViewTests
{
    private static readonly string[] PersonsAndWorkersRoles = ["Finance", "CarrierInvoices.Receive", "CarrierInvoices.Report", "CarrierInvoices.Manage"];

    private readonly FakeLogger<AgentCallTracking> _logger = new();

    private static List<RouteEndpoint> Endpoints()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddAuthorization();
        builder.Services.AddApiVersioning();
        builder.Services.AddSingleton(Substitute.For<IMediator>());
        var app = builder.Build();
        app.MapFinanceAgent();
        return [.. ((IEndpointRouteBuilder)app).DataSources.SelectMany(source => source.Endpoints).OfType<RouteEndpoint>()];
    }

    private static DefaultEndpointFilterInvocationContext ContextOf(string? azp, string? tool, string? agentHeader = null)
    {
        var http = new DefaultHttpContext();
        if (agentHeader is not null)
        {
            http.Request.Headers[AgentCallTracking.AgentHeader] = agentHeader;
        }

        if (azp is not null)
        {
            http.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("azp", azp)], "test"));
        }

        if (tool is not null)
        {
            http.Request.Headers[AgentCallTracking.ToolHeader] = tool;
        }

        return new DefaultEndpointFilterInvocationContext(http);
    }

    private Dictionary<string, object?> ScopeOf(string outcome) =>
        _logger.Collector.GetSnapshot()
            .Select(log => log.Scopes.OfType<IEnumerable<KeyValuePair<string, object?>>>().SelectMany(keys => keys).ToDictionary())
            .Last(keys => keys.TryGetValue("outcome", out var value) && Equals(value, outcome));

    [Fact]
    public void TheView_IsReadOnly_EveryRouteIsAGet_UnderTheAgentFinancePath()
    {
        var endpoints = Endpoints();

        endpoints.Should().NotBeEmpty();
        endpoints.Should().OnlyContain(endpoint => endpoint.RoutePattern.RawText!.Contains("/agent/finance/", StringComparison.Ordinal));
        endpoints.SelectMany(endpoint => endpoint.Metadata.OfType<HttpMethodMetadata>()).SelectMany(methods => methods.HttpMethods)
            .Should().OnlyContain(method => method == "GET");
    }

    [Fact]
    public void TheView_RequiresTheOneAgentReadRole_OnEveryRoute()
    {
        var endpoints = Endpoints();

        foreach (var endpoint in endpoints)
        {
            var roles = endpoint.Metadata.OfType<AuthorizationPolicy>().SelectMany(policy => policy.Requirements).OfType<Microsoft.AspNetCore.Authorization.Infrastructure.RolesAuthorizationRequirement>()
                .SelectMany(requirement => requirement.AllowedRoles).ToList();
            roles.Should().Equal(AgentRoles.FinanceRead);
        }
    }

    [Theory]
    [InlineData("CarrierInvoices.FinanceAgent.Read", true)]
    [InlineData("Finance", false)]
    [InlineData("CarrierInvoices.Manage", false)]
    [InlineData("CarrierInvoices.Receive", false)]
    [InlineData(null, false)]
    public async Task TheView_AdmitsOnlyACallerHoldingTheAgentReadRole(string? role, bool admitted)
    {
        var services = new ServiceCollection().AddLogging().AddAuthorization().BuildServiceProvider();
        var authorization = services.GetRequiredService<IAuthorizationService>();
        var claims = role is null ? [] : new[] { new Claim("roles", role) };
        var caller = new ClaimsPrincipal(new ClaimsIdentity(claims, "test", "name", "roles"));

        foreach (var endpoint in Endpoints())
        {
            var policy = endpoint.Metadata.OfType<AuthorizationPolicy>().Single();

            (await authorization.AuthorizeAsync(caller, policy)).Succeeded.Should().Be(admitted);
        }
    }

    [Fact]
    public void TheRole_IsTheOneTheToolListNames_AndHeldByNoPersonOrWorker()
    {
        AgentRoles.FinanceRead.Should().Be("CarrierInvoices.FinanceAgent.Read");
        PersonsAndWorkersRoles.Should().NotContain(AgentRoles.FinanceRead);
        typeof(AgentRoles).GetFields().Select(field => field.Name).Should().Equal([nameof(AgentRoles.FinanceRead)], "the agent holds one role and no other");
        FinanceAgentEndpoints.Flow.Should().Be("agent-view-finance-matching");
    }

    [Fact]
    public async Task GetInvoiceLine_AFoundLine_IsTheAgentView()
    {
        var mediator = Substitute.For<IMediator>();
        var view = new AgentInvoiceLineView("CAR-0001", "Van Dijk", "VDT-1", new DateOnly(2026, 10, 19), "TRX-1", "PriceMismatch", "ToDecide", 80m, "EUR", 0m, 10m, null, null, [], false);
#pragma warning disable CA2012 // NSubstitute configures the call by reading its return value, which is never consumed.
        mediator.Send(new GetAgentInvoiceLineQuery(new CarrierId("CAR-0001"), "VDT-1", "TRX-1"), Arg.Any<CancellationToken>()).Returns(ValueTask.FromResult(Result<AgentInvoiceLineView>.Success(view)));
#pragma warning restore CA2012

        var result = await FinanceAgentEndpoints.GetInvoiceLineAsync("CAR-0001", "VDT-1", "TRX-1", mediator, CancellationToken.None);

        result.Result.Should().BeOfType<Ok<AgentInvoiceLineView>>().Which.Value.Should().Be(view);
    }

    [Fact]
    public async Task GetInvoiceLine_ALineItDoesNotFind_IsANotFoundProblem()
    {
        var mediator = Substitute.For<IMediator>();
#pragma warning disable CA2012 // NSubstitute configures the call by reading its return value, which is never consumed.
        mediator.Send(Arg.Any<GetAgentInvoiceLineQuery>(), Arg.Any<CancellationToken>()).Returns(ValueTask.FromResult(Result<AgentInvoiceLineView>.NotFound()));
#pragma warning restore CA2012

        var result = await FinanceAgentEndpoints.GetInvoiceLineAsync("CAR-0001", "VDT-1", "NOPE", mediator, CancellationToken.None);

        result.Result.Should().BeOfType<ProblemHttpResult>().Which.StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task Invoke_ARead_IsTaggedWithTheAgentFromTheTokenAndTheToolFromTheServer_AndSucceeded()
    {
        var filter = new AgentCallTracking(Options.Create(new AgentApiOptions()), _logger);

        var result = await filter.InvokeAsync(ContextOf("norvia-local-agent-finance-matching", "get_invoice_line"), _ => ValueTask.FromResult<object?>(TypedResults.Ok("x")));

        result.Should().BeOfType<Ok<string>>();
        var keys = ScopeOf("Succeeded");
        keys["agentId"].Should().Be("norvia-local-agent-finance-matching");
        keys["toolName"].Should().Be("get_invoice_line");
    }

    [Fact]
    public async Task Invoke_BehindApiManagement_IsTaggedWithTheAgentTheGatewayNames()
    {
        var filter = new AgentCallTracking(Options.Create(new AgentApiOptions { GatewayClientId = "gateway-client" }), _logger);

        await filter.InvokeAsync(ContextOf("gateway-client", "get_invoice_line", "agent-identity"), _ => ValueTask.FromResult<object?>(TypedResults.Ok("x")));

        ScopeOf("Succeeded")["agentId"].Should().Be("agent-identity");
    }

    [Fact]
    public async Task Invoke_ACallerThatIsNotTheGateway_CannotNameTheAgentWithAHeader()
    {
        var filter = new AgentCallTracking(Options.Create(new AgentApiOptions { GatewayClientId = "gateway-client" }), _logger);

        await filter.InvokeAsync(ContextOf("someone-else", "get_invoice_line", "agent-identity"), _ => ValueTask.FromResult<object?>(TypedResults.Ok("x")));

        ScopeOf("Succeeded")["agentId"].Should().Be("someone-else");
    }

    [Fact]
    public async Task Invoke_ANotFound_IsRefused()
    {
        var filter = new AgentCallTracking(Options.Create(new AgentApiOptions()), _logger);

        await filter.InvokeAsync(ContextOf("agent", "get_invoice_line"), _ => ValueTask.FromResult<object?>(TypedResults.Problem(statusCode: 404)));

        ScopeOf("Refused").Should().ContainKey("agentId");
    }

    [Fact]
    public async Task Invoke_ACallWithNoAgentOrToolName_LeavesThoseKeysOut()
    {
        var filter = new AgentCallTracking(Options.Create(new AgentApiOptions()), _logger);

        await filter.InvokeAsync(ContextOf(null, null), _ => ValueTask.FromResult<object?>(TypedResults.Ok("x")));

        ScopeOf("Succeeded").Should().NotContainKeys("agentId", "toolName");
    }

    [Fact]
    public async Task Invoke_AToolNameThatIsFarTooLong_IsLeftOut()
    {
        var filter = new AgentCallTracking(Options.Create(new AgentApiOptions()), _logger);

        await filter.InvokeAsync(ContextOf("agent", new string('t', 500)), _ => ValueTask.FromResult<object?>(TypedResults.Ok("x")));

        ScopeOf("Succeeded").Should().NotContainKey("toolName");
    }
}
