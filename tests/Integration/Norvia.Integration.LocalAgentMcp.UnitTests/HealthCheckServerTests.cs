using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Norvia.Agents.Definitions;
using Norvia.Agents.Runner;
using Norvia.Agents.Tools;
using Norvia.Integration.LocalSupport;

namespace Norvia.Integration.LocalAgentMcp.UnitTests;

/// <summary>
/// The stand-in's second MCP server, the Estate health check's, at <c>/mcp/health-check</c> (ADR 0041). It makes the same checks in the same
/// order as the first, against its own audience, role, tool list and agent API.
/// </summary>
public sealed class HealthCheckServerTests : IAsyncLifetime
{
    private const string Findings = """{"estate":"as-found","checksRun":1,"findings":[{"id":"F-DLQ-ALERT"}]}""";

    private readonly LocalAgentMcpFactory _factory = new();

    public ValueTask InitializeAsync()
    {
        _factory.Console.Answer(HttpStatusCode.OK, Findings);
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

    private static Task<HttpResponseMessage> Post(HttpClient client, HttpContent content, string path = "/mcp/health-check") =>
        client.PostAsync(new Uri(path, UriKind.Relative), content, TestContext.Current.CancellationToken);

    private static JsonObject Call(string tool) => new() { ["name"] = tool, ["arguments"] = new JsonObject() };

    private static async Task<JsonNode> BodyAsync(HttpResponseMessage response) => JsonNode.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken))!;

    private Dictionary<string, object?> Keys(string outcome) =>
        _factory.Logs.GetSnapshot()
            .Select(log => log.Scopes.OfType<IEnumerable<KeyValuePair<string, object?>>>().SelectMany(keys => keys).ToDictionary())
            .Last(keys => keys.TryGetValue("outcome", out var value) && Equals(value, outcome));

    [Fact]
    public async Task ToolsList_IsExactlyTheToolsOfTheContractFile()
    {
        using var client = _factory.AgentClient(_factory.HealthCheckToken());

        using var response = await Post(client, Rpc("tools/list"));

        var tools = (await BodyAsync(response))["result"]!["tools"]!.AsArray();
        var contract = ToolListContract.Parse(Embedded("health-check.tools.json"));
        tools.Select(tool => tool!["name"]!.GetValue<string>()).Should().Equal(contract.Tools.Select(tool => tool.Name)).And.Equal("run_checks", "list_failed_runs", "list_dead_letter_queues");
        tools.Select(tool => tool!["description"]!.GetValue<string>()).Should().Equal(contract.Tools.Select(tool => tool.Description));
        tools[0]!["inputSchema"]!["required"]!.AsArray().Should().BeEmpty();
    }

    [Fact]
    public void TheAgentDefinition_ListsOnlyToolsTheStandInServes()
    {
        var definition = AgentDefinitionLoader.Parse(Embedded("estate-health-check.agent.yaml"));
        var contract = ToolListContract.Parse(Embedded("health-check.tools.json"));

        contract.Unknown(definition).Should().BeEmpty();
        contract.Tools.Should().OnlyContain(tool => tool.IsRead);
    }

    [Fact]
    public async Task ToolCall_CallsTheAppsAgentApiAsTheAgentIdentity_WithTheCorrelationIdTheAgentAndTheTool()
    {
        using var client = _factory.AgentClient(_factory.HealthCheckToken());
        client.DefaultRequestHeaders.Add("X-Correlation-ID", "run-1");

        using var response = await Post(client, Rpc("tools/call", Call("run_checks")));

        var result = (await BodyAsync(response))["result"]!;
        result["isError"]!.GetValue<bool>().Should().BeFalse();
        result["content"]![0]!["text"]!.GetValue<string>().Should().Be(Findings);
        var call = _factory.Console.Calls.Should().ContainSingle().Subject;
        call.RequestUri!.ToString().Should().Be("http://health-check/agent/v1/checks");
        call.Method.Should().Be(HttpMethod.Get);
        call.Headers.GetValues("X-Correlation-ID").Should().Equal("run-1");
        call.Headers.GetValues("X-Agent-ID").Should().Equal(LocalAgentMcpFactory.HealthCheckAgentId);
        call.Headers.GetValues("X-Tool-Name").Should().Equal("run_checks");
        var token = new Microsoft.IdentityModel.JsonWebTokens.JsonWebToken(call.Headers.Authorization!.Parameter);
        token.GetClaim("azp").Value.Should().Be(LocalAgentMcpFactory.HealthCheckAgentId);
        token.Audiences.Should().Equal(LocalAgentMcpFactory.HealthCheckApiAudience);
        token.Claims.Where(claim => claim.Type == "roles").Select(claim => claim.Value).Should().Equal(LocalAgentMcpFactory.HealthCheckRole);
    }

    [Fact]
    public async Task ToolCall_IsATraceOnTheMessageTrail_OfTheHealthCheckServersFlow_TaggedWithTheAgentAndTheTool()
    {
        using var client = _factory.AgentClient(_factory.HealthCheckToken());
        client.DefaultRequestHeaders.Add("X-Correlation-ID", "run-1");

        using var _ = await Post(client, Rpc("tools/call", Call("run_checks")));

        var keys = Keys(TrackingKeys.Succeeded);
        keys[TrackingKeys.CorrelationId].Should().Be("run-1");
        keys[TrackingKeys.Flow].Should().Be("agent-mcp-health-check");
        keys[TrackingKeys.AgentId].Should().Be(LocalAgentMcpFactory.HealthCheckAgentId);
        keys[TrackingKeys.ToolName].Should().Be("run_checks");
    }

    [Fact]
    public async Task ToolCall_ThatTheAgentApiRefuses_IsAnErrorResult_AndARefusedTrace()
    {
        _factory.Console.Answer(HttpStatusCode.Forbidden, "{}");
        using var client = _factory.AgentClient(_factory.HealthCheckToken());

        using var response = await Post(client, Rpc("tools/call", Call("run_checks")));

        (await BodyAsync(response))["result"]!["isError"]!.GetValue<bool>().Should().BeTrue();
        Keys(TrackingKeys.Refused)[TrackingKeys.FailureCode].Should().Be("AgentToolRefused");
    }

    [Fact]
    public async Task ToolCall_ForAToolOfTheOtherServer_IsRefused403_ToolNotOnList()
    {
        using var client = _factory.AgentClient(_factory.HealthCheckToken());

        using var response = await Post(client, Rpc("tools/call", Call("get_message_trail")));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        _factory.Console.Calls.Should().BeEmpty();
        Keys(TrackingKeys.Refused)[TrackingKeys.FailureCode].Should().Be("ToolNotOnList");
    }

    [Fact]
    public async Task TheOtherAgentsToken_IsRefused401_AtTheHealthCheckServer_AndTheHealthCheckAgentsAtTheOther()
    {
        using var integrationAgent = _factory.AgentClient();
        using var healthCheckAgent = _factory.AgentClient(_factory.HealthCheckToken());

        using var atHealthCheck = await Post(integrationAgent, Rpc("tools/list"));
        using var atIntegration = await Post(healthCheckAgent, Rpc("tools/list"), "/mcp");

        atHealthCheck.StatusCode.Should().Be(HttpStatusCode.Unauthorized, because: "each server has its own audience");
        atIntegration.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ARoleOfTheOtherServer_IsRefused403_AtTheHealthCheckServer()
    {
        using var client = _factory.AgentClient(_factory.HealthCheckToken(roles: [LocalAgentMcpFactory.Role]));

        using var response = await Post(client, Rpc("tools/list"));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        Keys(TrackingKeys.Refused)[TrackingKeys.FailureCode].Should().Be("AgentRoleMissing");
    }

    [Fact]
    public async Task ATokenWithoutAnAgentIdentity_IsRefused403()
    {
        using var client = _factory.AgentClient(_factory.HealthCheckToken(azp: null));

        using var response = await Post(client, Rpc("tools/list"));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task AServerTheStandInDoesNotHave_IsNotFound()
    {
        using var client = _factory.AgentClient(_factory.HealthCheckToken());

        using var response = await Post(client, Rpc("tools/list"), "/mcp/example");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task WhenTheHealthCheckIsNotConfigured_ItsServerIsNotServed_AndTheFirstStillIs()
    {
        using var without = new LocalAgentMcpFactory { ServesHealthCheck = false };
        using var client = without.AgentClient(without.HealthCheckToken());
        using var firstClient = without.AgentClient();

        using var health = await Post(client, Rpc("tools/list"));
        using var first = await Post(firstClient, Rpc("tools/list"), "/mcp");

        health.StatusCode.Should().Be(HttpStatusCode.NotFound);
        first.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task TheRateLimit_IsPerServer_SoOneServersBudgetDoesNotUseTheOthers()
    {
        using var limited = new LocalAgentMcpFactory { RateLimitCalls = 1 };
        using var agent = limited.AgentClient(limited.HealthCheckToken(azp: "shared-identity"));
        using var firstServerAgent = limited.AgentClient(limited.AgentToken(azp: "shared-identity"));

        (await Post(agent, Rpc("ping"))).StatusCode.Should().Be(HttpStatusCode.OK);
        using var over = await Post(agent, Rpc("ping"));
        using var otherServer = await Post(firstServerAgent, Rpc("ping"), "/mcp");

        over.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        otherServer.StatusCode.Should().Be(HttpStatusCode.OK, because: "each server has its own rate limit, as each has its own policy");
    }

    [Fact]
    public async Task TheMcpClientOfTheRunner_ListsAndCallsTheTool_ThroughTheStandIn()
    {
        using var http = _factory.AgentClient(_factory.HealthCheckToken());
        var source = new McpToolSource(http, new Uri(http.BaseAddress!, "/mcp/health-check"));

        await using var session = await source.OpenAsync(new ToolContext("run-1", "X-Correlation-ID"), TestContext.Current.CancellationToken);
        session.Tools.Select(candidate => candidate.Name).Should().Equal("run_checks", "list_failed_runs", "list_dead_letter_queues");
        var tool = session.Tools.Single(candidate => candidate.Name == "run_checks");
        var raw = await tool.InvokeAsync(new Microsoft.Extensions.AI.AIFunctionArguments(), TestContext.Current.CancellationToken);

        tool.Name.Should().Be("run_checks");
        raw.Should().NotBeNull();
        _factory.Console.Calls.Single().Headers.GetValues("X-Correlation-ID").Should().Equal("run-1");
    }

    private static string Embedded(string name)
    {
        using var stream = typeof(HealthCheckServerTests).Assembly.GetManifestResourceStream(name) ?? throw new InvalidOperationException($"{name} is not embedded.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
