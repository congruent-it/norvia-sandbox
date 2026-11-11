using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Norvia.Agents.Definitions;
using Norvia.Agents.Tools;

namespace Norvia.Integration.LocalAgentMcp.UnitTests;

/// <summary>
/// The stand-in's MCP server of the Planner agent, at <c>/mcp/planner</c> (#440, ADR 0041): one tool, the assignment options, behind an audience and a role of its own, so no other Agent's token opens anything here and the other way round.
/// </summary>
public sealed class PlannerServerTests : IAsyncLifetime
{
    private const string View = """{"transportOrderNumber":"TO-0000042","ownFleet":{"fit":"Fits"}}""";

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

    private static Task<HttpResponseMessage> Post(HttpClient client, HttpContent content, string path = "/mcp/planner") =>
        client.PostAsync(new Uri(path, UriKind.Relative), content, TestContext.Current.CancellationToken);

    private static JsonObject Call(string tool) =>
        new() { ["name"] = tool, ["arguments"] = new JsonObject { ["transportOrderNumber"] = "TO-0000042" } };

    [Fact]
    public async Task ToolsList_IsTheAssignmentOptionsAndTheExceptionTimeline()
    {
        using var client = _factory.AgentClient(_factory.PlannerToken());

        using var response = await Post(client, Rpc("tools/list"));

        var tools = JsonNode.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken))!["result"]!["tools"]!.AsArray();
        tools.Select(tool => tool!["name"]!.GetValue<string>()).Should().Equal("get_assignment_options", "get_exception_timeline");
    }

    [Fact]
    public async Task ToolCall_CallsThePlannerAgentViewAsThePlannerIdentity_WithItsOwnRole()
    {
        using var client = _factory.AgentClient(_factory.PlannerToken());
        client.DefaultRequestHeaders.Add("X-Correlation-ID", "order-1");

        using var response = await Post(client, Rpc("tools/call", Call("get_assignment_options")));

        JsonNode.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken))!["result"]!["content"]![0]!["text"]!.GetValue<string>().Should().Be(View);
        var call = _factory.Console.Calls.Should().ContainSingle().Subject;
        call.RequestUri!.ToString().Should().Be("http://tms-api/v1/agent/planner/transport-orders/TO-0000042/assignment-options");
        call.Headers.GetValues("X-Agent-ID").Should().Equal(LocalAgentMcpFactory.PlannerAgentId);
        call.Headers.GetValues("X-Tool-Name").Should().Equal("get_assignment_options");
        var token = new Microsoft.IdentityModel.JsonWebTokens.JsonWebToken(call.Headers.Authorization!.Parameter);
        token.Claims.Where(claim => claim.Type == "roles").Select(claim => claim.Value).Should().Equal(LocalAgentMcpFactory.PlannerRole);
    }

    [Fact]
    public async Task TheCustomerServiceAgentsToken_OpensNothingHere()
    {
        using var client = _factory.AgentClient(_factory.CustomerServiceToken());

        using var response = await Post(client, Rpc("tools/call", Call("get_assignment_options")));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized, "its audience is the Customer service agent's view");
        _factory.Console.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task ThePlannerAgentsToken_OpensNothingOnTheCustomerServiceServer()
    {
        using var client = _factory.AgentClient(_factory.PlannerToken());

        using var response = await Post(client, Rpc("tools/list"), "/mcp/customer-service");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        _factory.Console.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task ATokenForThePlannerViewWithoutTheRole_IsRefused403()
    {
        using var client = _factory.AgentClient(_factory.PlannerToken(roles: [LocalAgentMcpFactory.CustomerServiceRole]));

        using var response = await Post(client, Rpc("tools/call", Call("get_assignment_options")));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        _factory.Console.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task AToolOfTheCustomerServiceAgent_IsRefusedOnThePlannerServer()
    {
        using var client = _factory.AgentClient(_factory.PlannerToken());

        using var response = await Post(client, Rpc("tools/call", Call("get_exceptions")));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        _factory.Console.Calls.Should().BeEmpty();
    }

    [Fact]
    public void TheAgentDefinition_ListsOnlyTheOneReadTool_TheStandInServes()
    {
        var definition = AgentDefinitionLoader.Parse(Embedded("planner.agent.yaml"));
        var contract = ToolListContract.Parse(Embedded("planner.tools.json"));

        contract.Unknown(definition).Should().BeEmpty();
        contract.Tools.Should().OnlyContain(tool => tool.IsRead);
        contract.Role.Should().Be(LocalAgentMcpFactory.PlannerRole);
    }

    private static string Embedded(string name)
    {
        using var stream = typeof(PlannerServerTests).Assembly.GetManifestResourceStream(name) ?? throw new InvalidOperationException($"{name} is not embedded.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
