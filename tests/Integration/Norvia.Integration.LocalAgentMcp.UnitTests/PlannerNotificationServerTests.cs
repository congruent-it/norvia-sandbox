using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Norvia.Agents.Definitions;
using Norvia.Agents.Tools;

namespace Norvia.Integration.LocalAgentMcp.UnitTests;

/// <summary>
/// The stand-in's MCP server of the Planner notification agent, at <c>/mcp/planner-notification</c> (#444, ADR 0041): two tools, the Notify Shipper template and the Shipper view, behind an audience and a role of its own, so no other Agent's token opens anything here and the other way round.
/// </summary>
public sealed class PlannerNotificationServerTests : IAsyncLifetime
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

    private static Task<HttpResponseMessage> Post(HttpClient client, HttpContent content, string path = "/mcp/planner-notification") =>
        client.PostAsync(new Uri(path, UriKind.Relative), content, TestContext.Current.CancellationToken);

    private static JsonObject Call(string tool) =>
        new() { ["name"] = tool, ["arguments"] = new JsonObject { ["transportOrderNumber"] = "TO-0000042" } };

    [Fact]
    public async Task ToolsList_IsTheTemplateAndTheShipperView_AndNothingElse()
    {
        using var client = _factory.AgentClient(_factory.PlannerNotificationToken());

        using var response = await Post(client, Rpc("tools/list"));

        var tools = JsonNode.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken))!["result"]!["tools"]!.AsArray();
        tools.Select(tool => tool!["name"]!.GetValue<string>()).Should().Equal("get_shipper_notification_template", "get_shipper_view");
    }

    [Fact]
    public async Task ToolCall_CallsTheNotificationAgentViewAsItsOwnIdentity_WithItsOwnRole()
    {
        using var client = _factory.AgentClient(_factory.PlannerNotificationToken());
        client.DefaultRequestHeaders.Add("X-Correlation-ID", "order-1");

        using var response = await Post(client, Rpc("tools/call", Call("get_shipper_view")));

        JsonNode.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken))!["result"]!["content"]![0]!["text"]!.GetValue<string>().Should().Be(View);
        var call = _factory.Console.Calls.Should().ContainSingle().Subject;
        call.RequestUri!.ToString().Should().Be("http://tms-api/v1/agent/planner-notification/transport-orders/TO-0000042/shipper-view");
        call.Headers.GetValues("X-Agent-ID").Should().Equal(LocalAgentMcpFactory.PlannerNotificationAgentId);
        call.Headers.GetValues("X-Tool-Name").Should().Equal("get_shipper_view");
        var token = new Microsoft.IdentityModel.JsonWebTokens.JsonWebToken(call.Headers.Authorization!.Parameter);
        token.Claims.Where(claim => claim.Type == "roles").Select(claim => claim.Value).Should().Equal(LocalAgentMcpFactory.PlannerNotificationRole);
    }

    [Fact]
    public async Task TheCustomerServiceAgentsToken_OpensNothingHere()
    {
        using var client = _factory.AgentClient(_factory.CustomerServiceToken());

        using var response = await Post(client, Rpc("tools/call", Call("get_shipper_view")));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized, "its audience is the Customer service agent's view");
        _factory.Console.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task TheNotificationAgentsToken_OpensNothingOnTheCustomerServiceServer()
    {
        using var client = _factory.AgentClient(_factory.PlannerNotificationToken());

        using var response = await Post(client, Rpc("tools/list"), "/mcp/customer-service");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        _factory.Console.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task ATokenForTheNotificationViewWithoutTheRole_IsRefused403()
    {
        using var client = _factory.AgentClient(_factory.PlannerNotificationToken(roles: [LocalAgentMcpFactory.CustomerServiceRole]));

        using var response = await Post(client, Rpc("tools/call", Call("get_shipper_view")));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        _factory.Console.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task AToolOfTheCustomerServiceAgent_IsRefusedOnTheNotificationServer()
    {
        using var client = _factory.AgentClient(_factory.PlannerNotificationToken());

        using var response = await Post(client, Rpc("tools/call", Call("get_exceptions")));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        _factory.Console.Calls.Should().BeEmpty();
    }

    [Fact]
    public void TheAgentDefinition_ListsOnlyTheTwoReadTools_TheStandInServes()
    {
        var definition = AgentDefinitionLoader.Parse(Embedded("planner-notification.agent.yaml"));
        var contract = ToolListContract.Parse(Embedded("planner-notification.tools.json"));

        contract.Unknown(definition).Should().BeEmpty();
        contract.Tools.Should().OnlyContain(tool => tool.IsRead);
        contract.Role.Should().Be(LocalAgentMcpFactory.PlannerNotificationRole);
    }

    [Fact]
    public async Task TheTemplateTool_CallsTheTemplateRead_ByTheExceptionsNumber_AsTheNotificationIdentity()
    {
        _factory.Console.Answer(HttpStatusCode.OK, """{"message":"Your delivery failed."}""");
        using var client = _factory.AgentClient(_factory.PlannerNotificationToken());
        var arguments = new JsonObject { ["name"] = "get_shipper_notification_template", ["arguments"] = new JsonObject { ["exceptionNumber"] = "EX-0000005" } };

        using var response = await Post(client, Rpc("tools/call", arguments));

        JsonNode.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken))!["result"]!["content"]![0]!["text"]!.GetValue<string>().Should().Be("""{"message":"Your delivery failed."}""");
        _factory.Console.Calls.Should().ContainSingle().Which.RequestUri!.ToString().Should().Be("http://tms-api/v1/agent/planner-notification/exceptions/EX-0000005/shipper-notification-template");
    }

    [Fact]
    public async Task ThePlannerAgentsToken_OpensNothingHere()
    {
        using var client = _factory.AgentClient(_factory.PlannerToken());

        using var response = await Post(client, Rpc("tools/list"));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized, "its audience is the Planner agent's view");
        _factory.Console.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task AToolOfThePlannerAgent_IsRefusedOnTheNotificationServer()
    {
        using var client = _factory.AgentClient(_factory.PlannerNotificationToken());
        var arguments = new JsonObject { ["name"] = "get_exception_timeline", ["arguments"] = new JsonObject { ["exceptionNumber"] = "EX-0000005" } };

        using var response = await Post(client, Rpc("tools/call", arguments));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        _factory.Console.Calls.Should().BeEmpty();
    }

    private static string Embedded(string name)
    {
        using var stream = typeof(PlannerNotificationServerTests).Assembly.GetManifestResourceStream(name) ?? throw new InvalidOperationException($"{name} is not embedded.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
