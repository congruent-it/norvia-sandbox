using System.Net;
using System.Text;
using System.Text.Json.Nodes;

namespace Norvia.Integration.LocalAgentMcp.UnitTests;

/// <summary>
/// The stand-in's MCP server of the FinOps agent, at <c>/mcp/health-check-cost</c> (#511, ADR 0041): the two cost tools behind an audience and a role of its own, so the health-check agent's token opens nothing here and the other way round.
/// </summary>
public sealed class FinOpsServerTests : IAsyncLifetime
{
    private const string View = """{"periodDays":7,"total":{"name":"Total","change":"+80%"}}""";

    private readonly LocalAgentMcpFactory _factory = new();

    public ValueTask InitializeAsync()
    {
        _factory.Console.Answer(HttpStatusCode.OK, View);
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync() => await _factory.DisposeAsync();

    private static StringContent Rpc(string method, JsonObject? parameters = null)
    {
        var message = new JsonObject { ["jsonrpc"] = "2.0", ["id"] = 1, ["method"] = method };
        if (parameters is not null)
        {
            message["params"] = parameters;
        }

        return new StringContent(message.ToJsonString(), Encoding.UTF8, "application/json");
    }

    private static Task<HttpResponseMessage> Post(HttpClient client, HttpContent content, string path = "/mcp/health-check-cost") =>
        client.PostAsync(new Uri(path, UriKind.Relative), content, TestContext.Current.CancellationToken);

    private static JsonObject Call(string tool) => new() { ["name"] = tool, ["arguments"] = new JsonObject() };

    [Fact]
    public async Task ToolsList_IsTheTwoCostTools_AndNothingElse()
    {
        using var client = _factory.AgentClient(_factory.FinOpsToken());

        using var response = await Post(client, Rpc("tools/list"));

        var tools = JsonNode.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken))!["result"]!["tools"]!.AsArray();
        tools.Select(tool => tool!["name"]!.GetValue<string>()).Should().Equal("get_cost_view", "get_rising_spend_findings");
    }

    [Theory]
    [InlineData("get_cost_view", "http://health-check/agent/v1/cost")]
    [InlineData("get_rising_spend_findings", "http://health-check/agent/v1/rising-spend")]
    public async Task ToolCall_CallsTheHealthChecksAgentApiAsItsOwnIdentity_WithTheCostCallRole(string tool, string address)
    {
        using var client = _factory.AgentClient(_factory.FinOpsToken());
        client.DefaultRequestHeaders.Add("X-Correlation-ID", "spend-1");

        using var response = await Post(client, Rpc("tools/call", Call(tool)));

        JsonNode.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken))!["result"]!["content"]![0]!["text"]!.GetValue<string>().Should().Be(View);
        var call = _factory.Console.Calls.Should().ContainSingle().Subject;
        call.RequestUri!.ToString().Should().Be(address);
        call.Headers.GetValues("X-Agent-ID").Should().Equal(LocalAgentMcpFactory.FinOpsAgentId);
        call.Headers.GetValues("X-Tool-Name").Should().Equal(tool);
        var token = new Microsoft.IdentityModel.JsonWebTokens.JsonWebToken(call.Headers.Authorization!.Parameter);
        token.Claims.Where(claim => claim.Type == "roles").Select(claim => claim.Value).Should().Equal(LocalAgentMcpFactory.FinOpsRole);
    }

    [Fact]
    public async Task TheHealthCheckAgentsToken_OpensNothingHere_AndTheFinOpsAgentsTokenOpensNothingThere()
    {
        using var healthCheckAgent = _factory.AgentClient(_factory.HealthCheckToken());
        using var finOps = _factory.AgentClient(_factory.FinOpsToken());

        using var onCost = await Post(healthCheckAgent, Rpc("tools/call", Call("get_cost_view")));
        using var onHealthCheck = await Post(finOps, Rpc("tools/list"), "/mcp/health-check");

        onCost.StatusCode.Should().Be(HttpStatusCode.Unauthorized, "its audience is the health-check agent's view");
        onHealthCheck.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        _factory.Console.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task ATokenForTheRightAudienceWithoutTheCostRole_IsRefused403()
    {
        using var client = _factory.AgentClient(_factory.FinOpsToken(roles: ["HealthCheck.Agent.Read"]));

        using var response = await Post(client, Rpc("tools/call", Call("get_cost_view")));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        _factory.Console.Calls.Should().BeEmpty();
    }
}
