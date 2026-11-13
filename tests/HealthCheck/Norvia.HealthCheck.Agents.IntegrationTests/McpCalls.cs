using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;

namespace Norvia.HealthCheck.Agents.IntegrationTests;

/// <summary>The JSON-RPC calls the tests make to a server of the MCP stand-in, which each Agent's tests share.</summary>
internal static class McpCalls
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public static Task<HttpResponseMessage> CallAsync(HttpClient client, string path, string method, JsonObject? parameters = null) =>
        client.PostAsJsonAsync(
            new Uri(path, UriKind.Relative),
            new JsonObject { ["jsonrpc"] = "2.0", ["id"] = 1, ["method"] = method, ["params"] = parameters ?? new JsonObject() },
            Ct);

    /// <summary>Calls a tool and returns the text it answered with, the agent view's JSON.</summary>
    public static async Task<string> ToolTextAsync(HttpClient client, string path, string tool)
    {
        using var response = await CallAsync(client, path, "tools/call", new JsonObject { ["name"] = tool, ["arguments"] = new JsonObject() });
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var reply = (await response.Content.ReadFromJsonAsync<JsonObject>(Ct))!["result"]!;
        reply["isError"]!.GetValue<bool>().Should().BeFalse();
        return reply["content"]![0]!["text"]!.GetValue<string>();
    }
}
