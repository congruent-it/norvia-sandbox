using Microsoft.Extensions.AI;

namespace Norvia.Agents.Runner;

/// <summary>Where a run gets its tools: an MCP server in practice, anything with tools in a test.</summary>
public interface IToolSource
{
    Task<IToolSession> OpenAsync(ToolContext context, CancellationToken cancellationToken);
}

/// <summary>What a tool call carries so it joins the run's Message trail: the correlation ID, sent as the header the definition names.</summary>
public sealed record ToolContext(string CorrelationId, string CorrelationIdHeader);

/// <summary>The tools of one run. Disposing it closes the connection.</summary>
public interface IToolSession : IAsyncDisposable
{
    IReadOnlyList<AIFunction> Tools { get; }
}
