using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Norvia.Agents.Definitions;
using Norvia.Agents.Runner;
using Norvia.Agents.Scripts;

namespace Norvia.Tms.Api.IntegrationTests.CustomerServiceAgent;

/// <summary>What the tests do as the Shipper's booking, the Agent and its MCP server.</summary>
internal static class CustomerServiceAgentCalls
{
    public const string Tool = "get_rejected_booking";

    /// <summary>The flow the TMS API's agent view traces its calls under, and the stand-in's.</summary>
    public const string AgentViewFlow = "agent-view-customer-service";
    public const string StandInFlow = "agent-mcp-customer-service";

    /// <summary>What a Consignee and the Shipper's free text hold in <see cref="Bookings.Valid"/>, none of which the Agent may read.</summary>
    public static readonly string[] NeverForTheAgent =
    [
        "Café De Zwaan", "Sanne", "+31 30 555 0142", "Oudegracht", "3511 AW", "Waalhaven Oostzijde", "Ring at the side entrance",
    ];

    /// <summary>A Rejected booking of the test's own: a goods line that weighs nothing, with a Consignee and instructions the Agent must not read.</summary>
    public static async Task<RejectedBooking> RejectAsync(TmsFixture tms, CancellationToken cancellationToken)
    {
        var correlationId = Guid.NewGuid().ToString();
        var booking = Bookings.Valid($"BD-{Guid.NewGuid():N}"[..20]);
        booking["goodsLines"]![0]!["weightKg"] = 0;
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri("/v1/transport-orders", UriKind.Relative)) { Content = JsonContent.Create(booking) };
        request.Headers.Add("X-Correlation-ID", correlationId);
        using var rejection = await tms.Tms.SendAsync(request, cancellationToken);
        rejection.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity, await rejection.Content.ReadAsStringAsync(cancellationToken));

        var instance = JsonNode.Parse(await rejection.Content.ReadAsStringAsync(cancellationToken))!["instance"]!.GetValue<string>();
        var stored = JsonNode.Parse(await tms.Tms.GetStringAsync(new Uri(instance, UriKind.Relative), cancellationToken))!;
        return new RejectedBooking(stored["id"]!.GetValue<Guid>(), stored["correlationId"]!.GetValue<string>());
    }

    public sealed record RejectedBooking(Guid Id, string CorrelationId);

    /// <summary>A JSON-RPC call of the MCP protocol.</summary>
    public static Task<HttpResponseMessage> CallAsync(this HttpClient client, string method, CancellationToken cancellationToken, JsonObject? parameters = null) =>
        client.PostAsJsonAsync(
            new Uri(CustomerServiceAgentFixture.McpPath, UriKind.Relative),
            new JsonObject { ["jsonrpc"] = "2.0", ["id"] = 1, ["method"] = method, ["params"] = parameters ?? new JsonObject() },
            cancellationToken);

    /// <summary>Calls <c>get_rejected_booking</c> and returns the MCP result: its <c>isError</c> and the text the tool answered with.</summary>
    public static Task<(bool IsError, string Text)> GetRejectedBookingAsync(this HttpClient client, Guid id, string correlationId, CancellationToken cancellationToken) =>
        client.CallToolAsync(Tool, new JsonObject { ["id"] = id.ToString("D") }, correlationId, cancellationToken);

    /// <summary>Calls a tool and returns the MCP result: its <c>isError</c> and the text the tool answered with.</summary>
    public static async Task<(bool IsError, string Text)> CallToolAsync(
        this HttpClient client, string tool, JsonObject arguments, string correlationId, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(CustomerServiceAgentFixture.McpPath, UriKind.Relative))
        {
            Content = JsonContent.Create(new JsonObject
            {
                ["jsonrpc"] = "2.0",
                ["id"] = 1,
                ["method"] = "tools/call",
                ["params"] = new JsonObject { ["name"] = tool, ["arguments"] = arguments },
            }),
        };
        request.Headers.Add("X-Correlation-ID", correlationId);
        using var response = await client.SendAsync(request, cancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = (await response.Content.ReadFromJsonAsync<JsonObject>(cancellationToken))!["result"]!;
        return (result["isError"]!.GetValue<bool>(), result["content"]![0]!["text"]!.GetValue<string>());
    }

    /// <summary>The Agent's run, as the TMS UI runs it in process: its definition, the scripted model and the stand-in's tools, with the Agent's token.</summary>
    public static Task<AgentRunResult> ExplainAsync(CustomerServiceAgentFixture fixture, string agentId, Guid bookingId, string correlationId, CancellationToken cancellationToken) =>
        AskAsync(fixture, agentId, CustomerServiceQuestions.ExplainRejectedBooking(bookingId), correlationId, cancellationToken);

    /// <summary>The Agent's run for one of the fixed questions Customer service puts to it.</summary>
    public static async Task<AgentRunResult> AskAsync(CustomerServiceAgentFixture fixture, string agentId, string question, string correlationId, CancellationToken cancellationToken)
    {
        var definition = AgentDefinitionLoader.Parse(await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "agents", "customer-service.agent.yaml"), cancellationToken));
        using var http = new HttpClient();
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", fixture.AgentToken(agentId));
        var runner = new AgentRunner(CustomerServiceScript.Model(), new McpToolSource(http, fixture.McpAddress), new AgentRunnerOptions(TimeSpan.FromSeconds(30)));
        return await runner.RunAsync(
            definition,
            new AgentQuestion(question, correlationId, Guid.NewGuid().ToString("N")),
            cancellationToken);
    }
}
