using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Logging.Testing;
using Norvia.Tms.Api.Agents;
using Norvia.Tms.Application.Agents;

namespace Norvia.Tms.Api.UnitTests;

/// <summary>A call on the Customer service agent's view is a hop on the Rejected booking's Message trail, tagged with the Agent and the tool (coding guidelines §7).</summary>
public sealed class CustomerServiceAgentCallTests
{
    private static readonly string[] PersonsRoles = ["TransportOrders.Manage", "TransportOrders.Intake"];

    private readonly FakeLogger<AgentCallTracking> _logger = new();

    private static DefaultEndpointFilterInvocationContext ContextOf(string? azp, string? tool)
    {
        var http = new DefaultHttpContext();
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
    public async Task Invoke_ARead_IsTaggedWithTheAgentFromTheTokenAndTheToolFromTheServer_AndSucceeded()
    {
        var filter = new AgentCallTracking(_logger);

        var result = await filter.InvokeAsync(ContextOf("norvia-local-agent-customer-service", "get_rejected_booking"), _ => ValueTask.FromResult<object?>(TypedResults.Ok("x")));

        result.Should().BeOfType<Ok<string>>();
        var keys = ScopeOf("Succeeded");
        keys["agentId"].Should().Be("norvia-local-agent-customer-service");
        keys["toolName"].Should().Be("get_rejected_booking");
    }

    [Fact]
    public async Task Invoke_ANotFound_IsRefused()
    {
        var filter = new AgentCallTracking(_logger);

        await filter.InvokeAsync(ContextOf("agent", "get_rejected_booking"), _ => ValueTask.FromResult<object?>(TypedResults.Problem(statusCode: 404)));

        ScopeOf("Refused").Should().ContainKey("agentId");
    }

    [Fact]
    public async Task Invoke_ACallWithNoAgentOrToolName_LeavesThoseKeysOut()
    {
        var filter = new AgentCallTracking(_logger);

        await filter.InvokeAsync(ContextOf(null, null), _ => ValueTask.FromResult<object?>(TypedResults.Ok("x")));

        ScopeOf("Succeeded").Should().NotContainKeys("agentId", "toolName");
    }

    [Fact]
    public async Task Invoke_AToolNameThatIsFarTooLong_IsLeftOut()
    {
        var filter = new AgentCallTracking(_logger);

        await filter.InvokeAsync(ContextOf("agent", new string('t', 500)), _ => ValueTask.FromResult<object?>(TypedResults.Ok("x")));

        ScopeOf("Succeeded").Should().NotContainKey("toolName");
    }

    [Fact]
    public void TheRole_IsTheOneTheToolListNames_AndHeldByNoPersonsRole()
    {
        AgentRoles.CustomerServiceRead.Should().Be("TransportOrders.CustomerServiceAgent.Read");
        PersonsRoles.Should().NotContain(AgentRoles.CustomerServiceRead);
        CustomerServiceAgentEndpoints.Flow.Should().Be("agent-view-customer-service");
    }
}
