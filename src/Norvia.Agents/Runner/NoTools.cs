namespace Norvia.Agents.Runner;

/// <summary>The tool source of an Agent that has no tools (pure generation): every run gets an empty session.</summary>
public sealed class NoTools : IToolSource
{
    public static NoTools Source { get; } = new();

    public Task<IToolSession> OpenAsync(ToolContext context, CancellationToken cancellationToken) => Task.FromResult<IToolSession>(new Session());

    private sealed class Session : IToolSession
    {
        public IReadOnlyList<Microsoft.Extensions.AI.AIFunction> Tools => [];

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
