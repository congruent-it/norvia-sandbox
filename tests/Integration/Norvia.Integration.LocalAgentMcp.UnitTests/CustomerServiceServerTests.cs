using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Norvia.Agents.Definitions;
using Norvia.Agents.Tools;
using Norvia.Integration.LocalSupport;

namespace Norvia.Integration.LocalAgentMcp.UnitTests;

/// <summary>
/// The stand-in's third MCP server, the Customer service agent's, at <c>/mcp/customer-service</c> (ADR 0041). It makes the same checks in
/// the same order as the others, against its own audience and role, and calls the TMS API's agent view as the Agent's identity.
/// </summary>
public sealed class CustomerServiceServerTests : IAsyncLifetime
{
    private const string BookingId = "3f2b8c9e-6d5a-4c1e-9a77-0e1f2a3b4c5d";
    private const string Booking = """{"status":"Rejected","rejectionReasons":[{"code":"WeightInvalid"}]}""";

    private readonly LocalAgentMcpFactory _factory = new();

    public ValueTask InitializeAsync()
    {
        _factory.Console.Answer(HttpStatusCode.OK, Booking);
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

    private static Task<HttpResponseMessage> Post(HttpClient client, HttpContent content, string path = "/mcp/customer-service") =>
        client.PostAsync(new Uri(path, UriKind.Relative), content, TestContext.Current.CancellationToken);

    private static JsonObject Call(string tool, string? id = BookingId) =>
        new() { ["name"] = tool, ["arguments"] = id is null ? new JsonObject() : new JsonObject { ["id"] = id } };

    private static async Task<JsonNode> BodyAsync(HttpResponseMessage response) => JsonNode.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken))!;

    private Dictionary<string, object?> Keys(string outcome) =>
        _factory.Logs.GetSnapshot()
            .Select(log => log.Scopes.OfType<IEnumerable<KeyValuePair<string, object?>>>().SelectMany(keys => keys).ToDictionary())
            .Last(keys => keys.TryGetValue("outcome", out var value) && Equals(value, outcome));

    [Fact]
    public async Task ToolsList_IsExactlyTheToolOfTheContractFile()
    {
        using var client = _factory.AgentClient(_factory.CustomerServiceToken());

        using var response = await Post(client, Rpc("tools/list"));

        var tools = (await BodyAsync(response))["result"]!["tools"]!.AsArray();
        tools.Select(tool => tool!["name"]!.GetValue<string>()).Should().Equal("get_rejected_booking", "get_transport_order", "get_shipment_timeline", "get_exceptions", "get_proof_of_delivery_status");
        tools.Select(tool => tool!["inputSchema"]!["required"]!.AsArray().Single()!.GetValue<string>())
            .Should().Equal("id", "transportOrderNumber", "shipmentNumber", "shipmentNumber", "shipmentNumber");
    }

    [Fact]
    public void TheAgentDefinition_ListsOnlyReadToolsTheStandInServes()
    {
        var definition = AgentDefinitionLoader.Parse(Embedded("customer-service.agent.yaml"));
        var contract = ToolListContract.Parse(Embedded("customer-service.tools.json"));

        contract.Unknown(definition).Should().BeEmpty();
        contract.Tools.Should().OnlyContain(tool => tool.IsRead);
        contract.Role.Should().Be(LocalAgentMcpFactory.CustomerServiceRole);
    }

    [Fact]
    public async Task ToolCall_CallsTheTmsAgentViewAsTheAgentIdentity_WithTheCorrelationIdTheAgentAndTheTool()
    {
        using var client = _factory.AgentClient(_factory.CustomerServiceToken());
        client.DefaultRequestHeaders.Add("X-Correlation-ID", "booking-1");

        using var response = await Post(client, Rpc("tools/call", Call("get_rejected_booking")));

        var result = (await BodyAsync(response))["result"]!;
        result["isError"]!.GetValue<bool>().Should().BeFalse();
        result["content"]![0]!["text"]!.GetValue<string>().Should().Be(Booking);
        var call = _factory.Console.Calls.Should().ContainSingle().Subject;
        call.RequestUri!.ToString().Should().Be($"http://tms-api/v1/agent/customer-service/rejected-bookings/{BookingId}");
        call.Method.Should().Be(HttpMethod.Get);
        call.Headers.GetValues("X-Correlation-ID").Should().Equal("booking-1");
        call.Headers.GetValues("X-Agent-ID").Should().Equal(LocalAgentMcpFactory.CustomerServiceAgentId);
        call.Headers.GetValues("X-Tool-Name").Should().Equal("get_rejected_booking");
        var token = new Microsoft.IdentityModel.JsonWebTokens.JsonWebToken(call.Headers.Authorization!.Parameter);
        token.GetClaim("azp").Value.Should().Be(LocalAgentMcpFactory.CustomerServiceAgentId);
        token.Audiences.Should().Equal(LocalAgentMcpFactory.TmsAudience);
        token.Claims.Where(claim => claim.Type == "roles").Select(claim => claim.Value).Should().Equal(LocalAgentMcpFactory.CustomerServiceRole);
    }

    [Fact]
    public async Task ToolCall_IsATraceOnTheMessageTrail_OfTheCustomerServiceFlow_TaggedWithTheAgentAndTheTool()
    {
        using var client = _factory.AgentClient(_factory.CustomerServiceToken());
        client.DefaultRequestHeaders.Add("X-Correlation-ID", "booking-1");

        using var _ = await Post(client, Rpc("tools/call", Call("get_rejected_booking")));

        var keys = Keys(TrackingKeys.Succeeded);
        keys[TrackingKeys.CorrelationId].Should().Be("booking-1");
        keys[TrackingKeys.Flow].Should().Be("agent-mcp-customer-service");
        keys[TrackingKeys.AgentId].Should().Be(LocalAgentMcpFactory.CustomerServiceAgentId);
        keys[TrackingKeys.ToolName].Should().Be("get_rejected_booking");
    }

    [Fact]
    public async Task ToolCall_WhoseIdIsNotAPlainIdentifier_IsAnInvalidParamsError_AndCallsNothing()
    {
        using var client = _factory.AgentClient(_factory.CustomerServiceToken());

        using var response = await Post(client, Rpc("tools/call", Call("get_rejected_booking", "../../transport-orders")));

        (await BodyAsync(response))["error"]!["code"]!.GetValue<int>().Should().Be(-32602);
        _factory.Console.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task ToolCall_ThatTheAgentViewRefuses_IsAnErrorResult_AndARefusedTrace()
    {
        _factory.Console.Answer(HttpStatusCode.Forbidden, "{}");
        using var client = _factory.AgentClient(_factory.CustomerServiceToken());

        using var response = await Post(client, Rpc("tools/call", Call("get_rejected_booking")));

        (await BodyAsync(response))["result"]!["isError"]!.GetValue<bool>().Should().BeTrue();
        Keys(TrackingKeys.Refused)[TrackingKeys.FailureCode].Should().Be("AgentToolRefused");
    }

    [Fact]
    public async Task ToolCall_ForAToolOfAnotherServer_IsRefused403_ToolNotOnList()
    {
        using var client = _factory.AgentClient(_factory.CustomerServiceToken());

        using var response = await Post(client, Rpc("tools/call", Call("get_message_trail")));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        _factory.Console.Calls.Should().BeEmpty();
        Keys(TrackingKeys.Refused)[TrackingKeys.FailureCode].Should().Be("ToolNotOnList");
    }

    [Fact]
    public async Task AnotherAgentsToken_IsRefused401_AtTheCustomerServiceServer()
    {
        using var integrationAgent = _factory.AgentClient();

        using var response = await Post(integrationAgent, Rpc("tools/list"));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized, because: "each server has its own audience");
    }

    [Fact]
    public async Task ARoleOfAnotherServer_IsRefused403_AtTheCustomerServiceServer()
    {
        using var client = _factory.AgentClient(_factory.CustomerServiceToken(roles: [LocalAgentMcpFactory.Role]));

        using var response = await Post(client, Rpc("tools/list"));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        Keys(TrackingKeys.Refused)[TrackingKeys.FailureCode].Should().Be("AgentRoleMissing");
    }

    [Fact]
    public async Task TheRateLimit_RefusesACallOverTheBudget_With429()
    {
        using var limited = new LocalAgentMcpFactory { RateLimitCalls = 1 };
        using var agent = limited.AgentClient(limited.CustomerServiceToken());

        (await Post(agent, Rpc("ping"))).StatusCode.Should().Be(HttpStatusCode.OK);
        using var over = await Post(agent, Rpc("ping"));

        over.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
    }

    [Fact]
    public async Task WhenTheCustomerServiceIsNotConfigured_ItsServerIsNotServed_AndTheFirstStillIs()
    {
        using var without = new LocalAgentMcpFactory { ServesCustomerService = false };
        using var client = without.AgentClient(without.CustomerServiceToken());
        using var firstClient = without.AgentClient();

        using var customerService = await Post(client, Rpc("tools/list"));
        using var first = await Post(firstClient, Rpc("tools/list"), "/mcp");

        customerService.StatusCode.Should().Be(HttpStatusCode.NotFound);
        first.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private static string Embedded(string name)
    {
        using var stream = typeof(CustomerServiceServerTests).Assembly.GetManifestResourceStream(name) ?? throw new InvalidOperationException($"{name} is not embedded.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
