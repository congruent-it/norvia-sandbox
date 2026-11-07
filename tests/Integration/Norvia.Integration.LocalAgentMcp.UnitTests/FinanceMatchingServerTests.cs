using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Norvia.Agents.Definitions;
using Norvia.Agents.Tools;
using Norvia.Integration.LocalSupport;

namespace Norvia.Integration.LocalAgentMcp.UnitTests;

/// <summary>
/// The stand-in's MCP server for the Carrier invoice matching agent, at <c>/mcp/finance-matching</c> (ADR 0041). It makes the same checks in
/// the same order as the others (token, role, rate limit, then the call as the Agent's identity carrying the correlation ID), against its
/// own audience and role, and calls the ERP API's Finance agent view.
/// </summary>
public sealed class FinanceMatchingServerTests : IAsyncLifetime
{
    private const string Line = """{"result":"PriceMismatch","invoicedAmount":80.0}""";

    private readonly LocalAgentMcpFactory _factory = new();

    public ValueTask InitializeAsync()
    {
        _factory.Console.Answer(HttpStatusCode.OK, Line);
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

    private static Task<HttpResponseMessage> Post(HttpClient client, HttpContent content, string path = "/mcp/finance-matching") =>
        client.PostAsync(new Uri(path, UriKind.Relative), content, TestContext.Current.CancellationToken);

    private static JsonObject Call(string tool, string reference = "TRX-1") =>
        new() { ["name"] = tool, ["arguments"] = new JsonObject { ["carrierId"] = "CAR-0001", ["invoiceNumber"] = "VDT-1", ["carrierBookingReference"] = reference } };

    private static async Task<JsonNode> BodyAsync(HttpResponseMessage response) => JsonNode.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken))!;

    private Dictionary<string, object?> Keys(string outcome) =>
        _factory.Logs.GetSnapshot()
            .Select(log => log.Scopes.OfType<IEnumerable<KeyValuePair<string, object?>>>().SelectMany(keys => keys).ToDictionary())
            .Last(keys => keys.TryGetValue("outcome", out var value) && Equals(value, outcome));

    [Fact]
    public async Task ToolsList_IsExactlyTheReadToolsOfTheContractFile()
    {
        using var client = _factory.AgentClient(_factory.FinanceToken());

        using var response = await Post(client, Rpc("tools/list"));

        var tools = (await BodyAsync(response))["result"]!["tools"]!.AsArray();
        tools.Select(tool => tool!["name"]!.GetValue<string>()).Should().Equal("get_invoice_line", "get_invoice_summary");
        tools[0]!["inputSchema"]!["required"]!.AsArray().Select(name => name!.GetValue<string>()).Should().Equal("carrierId", "invoiceNumber", "carrierBookingReference");
        tools[1]!["inputSchema"]!["required"]!.AsArray().Select(name => name!.GetValue<string>()).Should().Equal("carrierId", "invoiceNumber");
    }

    [Fact]
    public void TheAgentDefinition_ListsOnlyReadToolsTheStandInServes()
    {
        var definition = AgentDefinitionLoader.Parse(Embedded("finance-matching.agent.yaml"));
        var contract = ToolListContract.Parse(Embedded("finance-matching.tools.json"));

        contract.Unknown(definition).Should().BeEmpty();
        contract.Tools.Should().OnlyContain(tool => tool.IsRead);
        contract.Role.Should().Be(LocalAgentMcpFactory.FinanceRole);
    }

    [Fact]
    public async Task ToolCall_CallsTheErpAgentViewAsTheAgentIdentity_WithTheCorrelationIdTheAgentAndTheTool()
    {
        using var client = _factory.AgentClient(_factory.FinanceToken());
        client.DefaultRequestHeaders.Add("X-Correlation-ID", "invoice-1");

        using var response = await Post(client, Rpc("tools/call", Call("get_invoice_line")));

        var result = (await BodyAsync(response))["result"]!;
        result["isError"]!.GetValue<bool>().Should().BeFalse();
        result["content"]![0]!["text"]!.GetValue<string>().Should().Be(Line);
        var call = _factory.Console.Calls.Should().ContainSingle().Subject;
        call.RequestUri!.ToString().Should().Be("http://erp-api/v1/agent/finance/carriers/CAR-0001/invoices/VDT-1/lines/TRX-1");
        call.Method.Should().Be(HttpMethod.Get);
        call.Headers.GetValues("X-Correlation-ID").Should().Equal("invoice-1");
        call.Headers.GetValues("X-Agent-ID").Should().Equal(LocalAgentMcpFactory.FinanceAgentId);
        call.Headers.GetValues("X-Tool-Name").Should().Equal("get_invoice_line");
        var token = new Microsoft.IdentityModel.JsonWebTokens.JsonWebToken(call.Headers.Authorization!.Parameter);
        token.GetClaim("azp").Value.Should().Be(LocalAgentMcpFactory.FinanceAgentId);
        token.Audiences.Should().Equal(LocalAgentMcpFactory.ErpAudience);
        token.Claims.Where(claim => claim.Type == "roles").Select(claim => claim.Value).Should().Equal(LocalAgentMcpFactory.FinanceRole);
    }

    [Fact]
    public async Task ToolCall_IsATraceOnTheMessageTrail_OfTheFinanceFlow_TaggedWithTheAgentAndTheTool()
    {
        using var client = _factory.AgentClient(_factory.FinanceToken());
        client.DefaultRequestHeaders.Add("X-Correlation-ID", "invoice-1");

        using var _ = await Post(client, Rpc("tools/call", Call("get_invoice_line")));

        var keys = Keys(TrackingKeys.Succeeded);
        keys[TrackingKeys.CorrelationId].Should().Be("invoice-1");
        keys[TrackingKeys.Flow].Should().Be("agent-mcp-finance-matching");
        keys[TrackingKeys.AgentId].Should().Be(LocalAgentMcpFactory.FinanceAgentId);
        keys[TrackingKeys.ToolName].Should().Be("get_invoice_line");
    }

    [Theory]
    [InlineData("TRX 0042")]
    [InlineData("../../carrier-invoices")]
    public async Task ToolCall_WhoseReferenceIsNotAPlainIdentifier_IsAnInvalidParamsError_AndCallsNothing(string reference)
    {
        using var client = _factory.AgentClient(_factory.FinanceToken());

        using var response = await Post(client, Rpc("tools/call", Call("get_invoice_line", reference)));

        (await BodyAsync(response))["error"]!["code"]!.GetValue<int>().Should().Be(-32602);
        _factory.Console.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task ToolCall_ThatTheAgentViewRefuses_IsAnErrorResult_AndARefusedTrace()
    {
        _factory.Console.Answer(HttpStatusCode.Forbidden, "{}");
        using var client = _factory.AgentClient(_factory.FinanceToken());

        using var response = await Post(client, Rpc("tools/call", Call("get_invoice_line")));

        (await BodyAsync(response))["result"]!["isError"]!.GetValue<bool>().Should().BeTrue();
        Keys(TrackingKeys.Refused)[TrackingKeys.FailureCode].Should().Be("AgentToolRefused");
    }

    [Fact]
    public async Task ToolCall_ForAToolThatWritesOrIsOfAnotherServer_IsRefused403_ToolNotOnList()
    {
        using var client = _factory.AgentClient(_factory.FinanceToken());

        using var response = await Post(client, Rpc("tools/call", Call("accept_invoice_line")));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        _factory.Console.Calls.Should().BeEmpty();
        Keys(TrackingKeys.Refused)[TrackingKeys.FailureCode].Should().Be("ToolNotOnList");
    }

    [Fact]
    public async Task AnotherAgentsToken_IsRefused401_AtTheFinanceServer()
    {
        using var customerService = _factory.AgentClient(_factory.CustomerServiceToken());

        using var response = await Post(customerService, Rpc("tools/list"));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized, because: "each server has its own audience");
    }

    [Fact]
    public async Task TheFinanceAgentsToken_IsRefused401_AtTheCustomerServiceServer()
    {
        using var finance = _factory.AgentClient(_factory.FinanceToken());

        using var response = await Post(finance, Rpc("tools/list"), "/mcp/customer-service");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized, because: "the Finance agent's token opens no other Agent's server");
    }

    [Fact]
    public async Task ARoleOfAnotherServer_IsRefused403_AtTheFinanceServer()
    {
        using var client = _factory.AgentClient(_factory.FinanceToken(roles: [LocalAgentMcpFactory.Role]));

        using var response = await Post(client, Rpc("tools/list"));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        Keys(TrackingKeys.Refused)[TrackingKeys.FailureCode].Should().Be("AgentRoleMissing");
    }

    [Fact]
    public async Task TheRateLimit_RefusesACallOverTheBudget_With429()
    {
        using var limited = new LocalAgentMcpFactory { RateLimitCalls = 1 };
        using var agent = limited.AgentClient(limited.FinanceToken());

        (await Post(agent, Rpc("ping"))).StatusCode.Should().Be(HttpStatusCode.OK);
        using var over = await Post(agent, Rpc("ping"));

        over.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
    }

    [Fact]
    public async Task WhenTheFinanceServerIsNotConfigured_ItIsNotServed_AndTheFirstStillIs()
    {
        using var without = new LocalAgentMcpFactory { ServesFinanceMatching = false };
        using var client = without.AgentClient(without.FinanceToken());
        using var firstClient = without.AgentClient();

        using var finance = await Post(client, Rpc("tools/list"));
        using var first = await Post(firstClient, Rpc("tools/list"), "/mcp");

        finance.StatusCode.Should().Be(HttpStatusCode.NotFound);
        first.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private static string Embedded(string name)
    {
        using var stream = typeof(FinanceMatchingServerTests).Assembly.GetManifestResourceStream(name) ?? throw new InvalidOperationException($"{name} is not embedded.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
