using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Norvia.Agents.Definitions;
using Norvia.Agents.Runner;
using Norvia.Agents.Tools;
using Norvia.Integration.LocalSupport;

namespace Norvia.Integration.LocalAgentMcp.UnitTests;

/// <summary>
/// The stand-in makes the checks of the API Management policy in its order (infra/estate/agent-mcp-server/mcp-server.policy.xml,
/// ADR 0041): token, read role, rate limit, tool list, and then the application's agent view as the Agent's identity.
/// </summary>
public sealed class McpEndpointTests : IAsyncLifetime
{
    private const string Trail = """{"correlationId":"c-1","hops":[]}""";

    private readonly LocalAgentMcpFactory _factory = new();

    public ValueTask InitializeAsync()
    {
        _factory.Console.Answer(HttpStatusCode.OK, Trail);
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync() => await _factory.DisposeAsync();

    private static StringContent Rpc(string method, JsonObject? parameters = null, int? id = 1)
    {
        var message = new JsonObject { ["jsonrpc"] = "2.0", ["method"] = method };
        if (id is not null)
        {
            message["id"] = id;
        }

        if (parameters is not null)
        {
            message["params"] = parameters;
        }

        return new StringContent(message.ToJsonString(), Encoding.UTF8, "application/json");
    }

    private static Task<HttpResponseMessage> Post(HttpClient client, HttpContent content) =>
        client.PostAsync(new Uri("/mcp", UriKind.Relative), content, TestContext.Current.CancellationToken);

    private static JsonObject Call(string tool, JsonObject? arguments = null) => new() { ["name"] = tool, ["arguments"] = arguments ?? new JsonObject { ["correlationId"] = "c-1" } };

    private static async Task<JsonNode> BodyAsync(HttpResponseMessage response) => JsonNode.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken))!;

    // The keys in scope when the stand-in traced, as the dashboard receives them.
    private Dictionary<string, object?> Keys(string outcome) =>
        _factory.Logs.GetSnapshot()
            .Select(log => log.Scopes.OfType<IEnumerable<KeyValuePair<string, object?>>>().SelectMany(keys => keys).ToDictionary())
            .Last(keys => keys.TryGetValue("outcome", out var value) && Equals(value, outcome));

    [Fact]
    public async Task ToolsList_IsExactlyTheToolsOfTheContractFile()
    {
        using var client = _factory.AgentClient();

        using var response = await Post(client, Rpc("tools/list"));

        var tools = (await BodyAsync(response))["result"]!["tools"]!.AsArray();
        var contract = ToolListContract.Parse(Embedded("integration-operations.tools.json"));
        tools.Select(tool => tool!["name"]!.GetValue<string>()).Should().Equal(contract.Tools.Select(tool => tool.Name));
        tools.Select(tool => tool!["description"]!.GetValue<string>()).Should().Equal(contract.Tools.Select(tool => tool.Description));
        tools[1]!["inputSchema"]!["required"]!.AsArray().Select(name => name!.GetValue<string>()).Should().Equal("logicApp", "workflow", "runId");
    }

    [Fact]
    public void TheAgentDefinition_ListsOnlyToolsTheStandInServes()
    {
        var definition = AgentDefinitionLoader.Parse(Embedded("integration-operations.agent.yaml"));
        var contract = ToolListContract.Parse(Embedded("integration-operations.tools.json"));

        contract.Unknown(definition).Should().BeEmpty();
        contract.Tools.Should().OnlyContain(tool => tool.IsRead);
    }

    [Fact]
    public async Task Initialize_AnswersWithTheProtocolVersionTheClientAskedFor_AndAToolsCapability()
    {
        using var client = _factory.AgentClient();

        using var response = await Post(client, Rpc("initialize", new JsonObject { ["protocolVersion"] = "2025-03-26" }));

        var result = (await BodyAsync(response))["result"]!;
        result["protocolVersion"]!.GetValue<string>().Should().Be("2025-03-26");
        result["capabilities"]!["tools"].Should().NotBeNull();
    }

    [Fact]
    public async Task Notification_IsAccepted_AndPing_Answered_AndAnUnknownMethod_IsAnError()
    {
        using var client = _factory.AgentClient();
        var mcp = new Uri("/mcp", UriKind.Relative);

        using var notification = await Post(client, Rpc("notifications/initialized", id: null));
        using var ping = await Post(client, Rpc("ping"));
        using var unknown = await Post(client, Rpc("resources/list"));

        notification.StatusCode.Should().Be(HttpStatusCode.Accepted);
        (await BodyAsync(ping))["result"].Should().NotBeNull();
        (await BodyAsync(unknown))["error"]!["code"]!.GetValue<int>().Should().Be(-32601, because: "only tools are supported (ADR 0040)");
    }

    [Fact]
    public async Task AMessageThatIsNotJsonRpc_IsRefused400()
    {
        using var client = _factory.AgentClient();

        using var notJson = await Post(client, new StringContent("nope", Encoding.UTF8, "application/json"));
        using var noMethod = await Post(client, new StringContent("{}", Encoding.UTF8, "application/json"));

        notJson.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        noMethod.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task TheOptionalStream_IsRefused405_AndASessionCanBeClosed()
    {
        using var client = _factory.AgentClient();

        using var stream = await client.GetAsync(new Uri("/mcp", UriKind.Relative), TestContext.Current.CancellationToken);
        using var close = await client.DeleteAsync(new Uri("/mcp", UriKind.Relative), TestContext.Current.CancellationToken);

        stream.StatusCode.Should().Be(HttpStatusCode.MethodNotAllowed);
        close.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task ToolCall_CallsTheAgentViewAsTheAgentIdentity_WithTheCorrelationIdTheAgentAndTheTool()
    {
        using var client = _factory.AgentClient();
        client.DefaultRequestHeaders.Add("X-Correlation-ID", "c-1");

        using var response = await Post(client, Rpc("tools/call", Call("get_message_trail")));

        var result = (await BodyAsync(response))["result"]!;
        result["isError"]!.GetValue<bool>().Should().BeFalse();
        result["content"]![0]!["text"]!.GetValue<string>().Should().Be(Trail);
        var call = _factory.Console.Calls.Should().ContainSingle().Subject;
        call.RequestUri!.ToString().Should().Be("http://integration-console/agent/v1/trails/c-1");
        call.Method.Should().Be(HttpMethod.Get);
        call.Headers.GetValues("X-Correlation-ID").Should().Equal("c-1");
        call.Headers.GetValues("X-Agent-ID").Should().Equal(LocalAgentMcpFactory.AgentId);
        call.Headers.GetValues("X-Tool-Name").Should().Equal("get_message_trail");
        var token = new Microsoft.IdentityModel.JsonWebTokens.JsonWebToken(call.Headers.Authorization!.Parameter);
        token.GetClaim("azp").Value.Should().Be(LocalAgentMcpFactory.AgentId);
        token.Audiences.Should().Equal(LocalAgentMcpFactory.ConsoleAudience);
        token.Claims.Where(claim => claim.Type == "roles").Select(claim => claim.Value).Should().Equal(LocalAgentMcpFactory.Role);
    }

    [Fact]
    public async Task ToolCall_IsATraceOnTheMessageTrail_TaggedWithTheAgentAndTheTool()
    {
        using var client = _factory.AgentClient();
        client.DefaultRequestHeaders.Add("X-Correlation-ID", "c-1");

        using var _ = await Post(client, Rpc("tools/call", Call("get_message_trail")));

        var keys = Keys(TrackingKeys.Succeeded);
        keys[TrackingKeys.CorrelationId].Should().Be("c-1");
        keys[TrackingKeys.Flow].Should().Be("agent-mcp-integration-operations");
        keys[TrackingKeys.AgentId].Should().Be(LocalAgentMcpFactory.AgentId);
        keys[TrackingKeys.ToolName].Should().Be("get_message_trail");
    }

    [Fact]
    public async Task ToolCall_WithoutACorrelationIdFromTheAgent_GetsANewOne_AndReturnsIt()
    {
        using var client = _factory.AgentClient();

        using var response = await Post(client, Rpc("tools/call", Call("get_message_trail")));

        var id = response.Headers.GetValues("X-Correlation-ID").Single();
        Guid.TryParse(id, out _).Should().BeTrue();
        _factory.Console.Calls.Single().Headers.GetValues("X-Correlation-ID").Should().Equal(id);
    }

    [Fact]
    public async Task ToolCall_WithACorrelationIdThatIsNotAnIdentifier_GetsANewOne()
    {
        using var client = _factory.AgentClient();
        client.DefaultRequestHeaders.Add("X-Correlation-ID", "a b<script>");

        using var response = await Post(client, Rpc("tools/call", Call("get_message_trail")));

        Guid.TryParse(response.Headers.GetValues("X-Correlation-ID").Single(), out _).Should().BeTrue();
    }

    [Fact]
    public async Task ToolCall_ForARun_PutsEachInputInItsSegment()
    {
        using var client = _factory.AgentClient();
        var arguments = new JsonObject { ["logicApp"] = "logic-booking", ["workflow"] = "carrier-booking", ["runId"] = "08584" };

        using var _ = await Post(client, Rpc("tools/call", Call("get_run", arguments)));

        _factory.Console.Calls.Single().RequestUri!.AbsolutePath.Should().Be("/agent/v1/runs/logic-booking/carrier-booking/08584");
    }

    [Theory]
    [InlineData("carrier-tracking", "carrier-tracking")]
    [InlineData("shipment-events/billing", "shipment-events%2Fbilling")]
    public async Task ToolCall_ForADeadLetterQueue_PutsTheQueueOrTopicAndSubscriptionInOneSegment(string entity, string segment)
    {
        using var client = _factory.AgentClient();

        using var _ = await Post(client, Rpc("tools/call", Call("peek_dead_letters", new JsonObject { ["entity"] = entity })));

        _factory.Console.Calls.Single().RequestUri!.AbsolutePath.Should().Be($"/agent/v1/dead-letter-queues/{segment}/messages");
    }

    [Theory]
    [InlineData("/")]
    [InlineData("a//b")]
    [InlineData("../b")]
    [InlineData("a/..")]
    [InlineData("a/b?c")]
    public async Task ToolCall_ForADeadLetterQueue_WithANameThatIsNotATopicAndSubscription_IsAnInvalidParams(string entity)
    {
        using var client = _factory.AgentClient();

        using var response = await Post(client, Rpc("tools/call", Call("peek_dead_letters", new JsonObject { ["entity"] = entity })));

        (await BodyAsync(response))["error"]!["code"]!.GetValue<int>().Should().Be(-32602);
        _factory.Console.Calls.Should().BeEmpty();
    }

    [Theory]
    [InlineData("list_failed_runs", "/agent/v1/failed-runs")]
    [InlineData("list_dead_letter_queues", "/agent/v1/dead-letter-queues")]
    public async Task ToolCall_ForAToolWithNoInputs_CallsItsRoute(string tool, string path)
    {
        using var client = _factory.AgentClient();

        using var _ = await Post(client, Rpc("tools/call", Call(tool)));

        _factory.Console.Calls.Single().RequestUri!.AbsolutePath.Should().Be(path);
    }

    [Theory]
    [InlineData("../admin")]
    [InlineData("a/b")]
    [InlineData("a?b=c")]
    [InlineData("")]
    public async Task ToolCall_WithAnInputThatIsNotAPlainIdentifier_IsAnInvalidParams_AndTheAgentViewIsNotCalled(string correlationId)
    {
        using var client = _factory.AgentClient();

        using var response = await Post(client, Rpc("tools/call", Call("get_message_trail", new JsonObject { ["correlationId"] = correlationId })));

        (await BodyAsync(response))["error"]!["code"]!.GetValue<int>().Should().Be(-32602);
        _factory.Console.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task ToolCall_WithAMissingInput_IsAnInvalidParams()
    {
        using var client = _factory.AgentClient();

        using var response = await Post(client, Rpc("tools/call", Call("get_run", new JsonObject { ["logicApp"] = "x" })));

        (await BodyAsync(response))["error"]!["code"]!.GetValue<int>().Should().Be(-32602);
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task ToolCall_TheAgentViewRefusesOrFails_IsAnErrorResultToTheAgent_TracedAgentToolRefused(HttpStatusCode status)
    {
        _factory.Console.Answer(status, "{}");
        using var client = _factory.AgentClient();

        using var response = await Post(client, Rpc("tools/call", Call("get_message_trail")));

        (await BodyAsync(response))["result"]!["isError"]!.GetValue<bool>().Should().BeTrue();
        Keys(TrackingKeys.Refused)[TrackingKeys.FailureCode].Should().Be("AgentToolRefused");
    }

    [Fact]
    public async Task ToolCall_TheAgentViewIsUnreachable_IsAnErrorResultToTheAgent()
    {
        _factory.Console.Fail(new HttpRequestException("connection refused"));
        using var client = _factory.AgentClient();

        using var response = await Post(client, Rpc("tools/call", Call("get_message_trail")));

        (await BodyAsync(response))["result"]!["isError"]!.GetValue<bool>().Should().BeTrue();
        Keys(TrackingKeys.Refused)[TrackingKeys.FailureCode].Should().Be("AgentToolRefused");
    }

    [Fact]
    public async Task ToolCall_TheAgentViewIsTooSlow_IsAnErrorResultToTheAgent()
    {
        _factory.Console.Fail(new TaskCanceledException("timed out"));
        using var client = _factory.AgentClient();

        using var response = await Post(client, Rpc("tools/call", Call("get_message_trail")));

        (await BodyAsync(response))["result"]!["isError"]!.GetValue<bool>().Should().BeTrue();
    }

    [Fact]
    public async Task WithoutAToken_IsRefused401_AndNothingIsCalled()
    {
        using var client = _factory.CreateClient();

        using var response = await Post(client, Rpc("tools/call", Call("get_message_trail")));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        _factory.Console.Calls.Should().BeEmpty();
        Keys(TrackingKeys.Refused)[TrackingKeys.FailureCode].Should().Be("AgentTokenInvalid");
    }

    [Fact]
    public async Task ATokenForAnotherAudience_IsRefused401()
    {
        using var client = _factory.AgentClient(_factory.AgentToken(audience: "api://another"));

        using var response = await Post(client, Rpc("tools/list"));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ATokenSignedWithAnotherKey_IsRefused401()
    {
        using var client = _factory.AgentClient(_factory.AgentToken(signingKey: new byte[64]));

        using var response = await Post(client, Rpc("tools/list"));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ATokenWithoutTheReadRole_IsRefused403_AgentRoleMissing_TracedWithTheAgent()
    {
        using var client = _factory.AgentClient(_factory.AgentToken(roles: ["Something.Else"]));

        using var response = await Post(client, Rpc("tools/call", Call("get_message_trail")));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await BodyAsync(response))["messageId"]!.GetValue<string>().Should().Be(response.Headers.GetValues("X-Correlation-ID").Single());
        _factory.Console.Calls.Should().BeEmpty();
        var keys = Keys(TrackingKeys.Refused);
        keys[TrackingKeys.FailureCode].Should().Be("AgentRoleMissing");
        keys[TrackingKeys.AgentId].Should().Be(LocalAgentMcpFactory.AgentId);
    }

    [Fact]
    public async Task ATokenWithoutAnAgentIdentity_IsRefused403_NoAgentIdentity()
    {
        using var client = _factory.AgentClient(_factory.AgentToken(azp: null));

        using var response = await Post(client, Rpc("tools/list"));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        Keys(TrackingKeys.Refused)[TrackingKeys.FailureCode].Should().Be("NoAgentIdentity");
    }

    [Fact]
    public async Task ATool_NotOnTheList_IsRefused403_ToolNotOnList_EvenForAnAgentWithTheRole()
    {
        using var client = _factory.AgentClient();

        using var response = await Post(client, Rpc("tools/call", Call("delete_run")));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        _factory.Console.Calls.Should().BeEmpty();
        var keys = Keys(TrackingKeys.Refused);
        keys[TrackingKeys.FailureCode].Should().Be("ToolNotOnList");
        keys[TrackingKeys.ToolName].Should().Be("delete_run");
    }

    [Fact]
    public async Task ACallWithoutAToolName_IsRefused403_ToolNotOnList()
    {
        using var client = _factory.AgentClient();

        using var response = await Post(client, Rpc("tools/call", new JsonObject()));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task TheRateLimit_IsPerAgentIdentity_RenewsWithTheWindow_AndComesAfterTheRoleCheck()
    {
        using var limited = new LocalAgentMcpFactory { RateLimitCalls = 2 };
        limited.Console.Answer(HttpStatusCode.OK, Trail);
        using var agent = limited.AgentClient();
        using var other = limited.AgentClient(limited.AgentToken(azp: "another-agent"));
        using var withoutRole = limited.AgentClient(limited.AgentToken(roles: []));
        var mcp = new Uri("/mcp", UriKind.Relative);

        (await Post(agent, Rpc("ping"))).StatusCode.Should().Be(HttpStatusCode.OK);
        (await Post(agent, Rpc("ping"))).StatusCode.Should().Be(HttpStatusCode.OK);
        using var over = await Post(agent, Rpc("ping"));
        using var otherAgent = await Post(other, Rpc("ping"));
        using var noRole = await Post(withoutRole, Rpc("ping"));
        limited.Time.Advance(TimeSpan.FromSeconds(61));
        using var renewed = await Post(agent, Rpc("ping"));

        over.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        over.Headers.RetryAfter.Should().NotBeNull();
        otherAgent.StatusCode.Should().Be(HttpStatusCode.OK, because: "each Agent identity has its own budget");
        noRole.StatusCode.Should().Be(HttpStatusCode.Forbidden, because: "the role is checked before the rate limit");
        renewed.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task TheMcpClientOfTheRunner_ListsAndCallsTheTools_ThroughTheStandIn()
    {
        using var http = _factory.AgentClient();
        var source = new McpToolSource(http, new Uri(http.BaseAddress!, "/mcp"));

        await using var session = await source.OpenAsync(new ToolContext("c-1", "X-Correlation-ID"), TestContext.Current.CancellationToken);
        var raw = await session.Tools.Single(tool => tool.Name == "get_message_trail")
            .InvokeAsync(new Microsoft.Extensions.AI.AIFunctionArguments { ["correlationId"] = "c-1" }, TestContext.Current.CancellationToken);

        session.Tools.Select(tool => tool.Name).Should().Equal("get_message_trail", "get_run", "list_failed_runs", "list_dead_letter_queues", "peek_dead_letters");
        raw.Should().NotBeNull();
        _factory.Console.Calls.Single().Headers.GetValues("X-Correlation-ID").Should().Equal("c-1");
    }

    private static string Embedded(string name)
    {
        using var stream = typeof(McpEndpointTests).Assembly.GetManifestResourceStream(name) ?? throw new InvalidOperationException($"{name} is not embedded.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
