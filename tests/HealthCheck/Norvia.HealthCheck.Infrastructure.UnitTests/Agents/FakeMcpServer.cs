using System.Net;
using System.Text;
using System.Text.Json.Nodes;

namespace Norvia.HealthCheck.Infrastructure.UnitTests.Agents;

/// <summary>An MCP server over streamable HTTP with the Estate health-check agent's one tool, answered from the test.</summary>
internal sealed class FakeMcpServer : HttpMessageHandler
{
    public const string OneFinding = """
        {"estate":"as-found","checksRun":1,"findings":[{"id":"F-DLQ-ALERT","checkId":"DLQ-ALERT","title":"A dead-letter queue has no alert","rule":"Every queue has an alert.","severity":"High","evidence":[{"subject":"carrier-tracking","detail":"No alert."}],"asFoundToggle":"entitiesWithoutDeadLetterAlert","fix":"Remove it."}]}
        """;

    public const string NoFinding = """{"estate":"fixed","checksRun":1,"findings":[]}""";

    public string Answer { get; set; } = OneFinding;

    public bool ToolsRefused { get; set; }

    public Exception? Unreachable { get; set; }

    public List<string> Authorizations { get; } = [];

    public List<string> CorrelationIds { get; } = [];

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (Unreachable is not null)
        {
            throw Unreachable;
        }

        Authorizations.AddRange(request.Headers.Authorization is { } authorization ? [authorization.ToString()] : []);
        CorrelationIds.AddRange(request.Headers.TryGetValues("X-Correlation-ID", out var ids) ? ids : []);
        if (request.Method != HttpMethod.Post)
        {
            return new HttpResponseMessage(request.Method == HttpMethod.Get ? HttpStatusCode.MethodNotAllowed : HttpStatusCode.OK);
        }

        var message = JsonNode.Parse(await request.Content!.ReadAsStringAsync(cancellationToken))!;
        var id = message["id"]?.DeepClone();
        if (id is null)
        {
            return new HttpResponseMessage(HttpStatusCode.Accepted);
        }

        JsonNode? result = message["method"]!.GetValue<string>() switch
        {
            "initialize" => JsonNode.Parse("""{"protocolVersion":"2025-06-18","capabilities":{"tools":{}},"serverInfo":{"name":"fake","version":"1"}}"""),
            "tools/list" => JsonNode.Parse("""{"tools":[{"name":"run_checks","description":"d","inputSchema":{"type":"object","properties":{},"required":[]}}]}"""),
            "tools/call" => new JsonObject
            {
                ["content"] = new JsonArray(new JsonObject { ["type"] = "text", ["text"] = ToolsRefused ? "The agent view answered 403." : Answer }),
                ["isError"] = ToolsRefused,
            },
            _ => null,
        };

        var body = new JsonObject { ["jsonrpc"] = "2.0", ["id"] = id };
        if (result is null)
        {
            body["error"] = new JsonObject { ["code"] = -32601, ["message"] = "Method not found" };
        }
        else
        {
            body["result"] = result;
        }

        return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json") };
    }
}
