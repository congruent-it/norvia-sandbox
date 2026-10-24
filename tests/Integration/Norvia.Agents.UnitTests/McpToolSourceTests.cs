using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Norvia.Agents.Runner;

namespace Norvia.Agents.UnitTests;

public sealed class McpToolSourceTests
{
    private static readonly Uri Endpoint = new("http://mcp.test/mcp");

    [Fact]
    public async Task OpenAsync_ListsTheServersTools_AndCallsThemWithTheCorrelationIdHeader()
    {
        var server = new FakeMcpServer();
        using var http = new HttpClient(server);
        var source = new McpToolSource(http, Endpoint);

        await using var session = await source.OpenAsync(new ToolContext("c-1", "X-Correlation-ID"), CancellationToken.None);
        var raw = await session.Tools.Single(tool => tool.Name == "get_message_trail")
            .InvokeAsync(new Microsoft.Extensions.AI.AIFunctionArguments { ["correlationId"] = "c-1" }, CancellationToken.None);

        session.Tools.Select(tool => tool.Name).Should().Equal("get_message_trail");
        ToolResults.TextOf(raw, out var isError).Should().Be("""{"hops":[]}""");
        isError.Should().BeFalse();
        server.CorrelationIds.Should().NotBeEmpty().And.OnlyContain(id => id == "c-1");
    }

    private sealed class FakeMcpServer : HttpMessageHandler
    {
        public List<string> CorrelationIds { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.Headers.TryGetValues("X-Correlation-ID", out var ids))
            {
                CorrelationIds.AddRange(ids);
            }

            if (request.Method != HttpMethod.Post)
            {
                return new HttpResponseMessage(request.Method == HttpMethod.Get ? HttpStatusCode.MethodNotAllowed : HttpStatusCode.OK);
            }

            var message = JsonNode.Parse(await request.Content!.ReadAsStringAsync(cancellationToken))!;
            var id = message["id"]?.DeepClone();
            JsonNode? result = message["method"]!.GetValue<string>() switch
            {
                "initialize" => JsonNode.Parse("""{"protocolVersion":"2025-06-18","capabilities":{"tools":{}},"serverInfo":{"name":"fake","version":"1"}}"""),
                "tools/list" => JsonNode.Parse("""{"tools":[{"name":"get_message_trail","description":"d","inputSchema":{"type":"object","properties":{"correlationId":{"type":"string"}},"required":["correlationId"]}}]}"""),
                "tools/call" => JsonNode.Parse("""{"content":[{"type":"text","text":"{\"hops\":[]}"}]}"""),
                _ => null,
            };

            if (id is null)
            {
                return new HttpResponseMessage(HttpStatusCode.Accepted);
            }

            // A request the server doesn't know, such as the client's first try at discovery, is answered with an error and the client falls back.
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
}
