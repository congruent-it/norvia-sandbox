using Microsoft.Extensions.AI;
using ModelContextProtocol.Client;

namespace Norvia.Agents.Runner;

/// <summary>
/// An MCP server's tools, over streamable HTTP. The correlation ID goes down as a header on every request, as Foundry
/// sends it from the definition's structured input. The caller's <see cref="HttpClient"/> carries the Agent's token.
/// </summary>
public sealed class McpToolSource(HttpClient http, Uri endpoint) : IToolSource
{
    public async Task<IToolSession> OpenAsync(ToolContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        var transport = new HttpClientTransport(
            new HttpClientTransportOptions
            {
                Endpoint = endpoint,
                TransportMode = HttpTransportMode.StreamableHttp,
                AdditionalHeaders = new Dictionary<string, string> { [context.CorrelationIdHeader] = context.CorrelationId },
            },
            http,
            ownsHttpClient: false);
        var client = await McpClient.CreateAsync(transport, cancellationToken: cancellationToken);
        try
        {
            return new Session(client, [.. await client.ListToolsAsync(cancellationToken: cancellationToken)]);
        }
        catch
        {
            await client.DisposeAsync();
            throw;
        }
    }

    private sealed class Session(McpClient client, IReadOnlyList<AIFunction> tools) : IToolSession
    {
        public IReadOnlyList<AIFunction> Tools => tools;

        public ValueTask DisposeAsync() => client.DisposeAsync();
    }
}
