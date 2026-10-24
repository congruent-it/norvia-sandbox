using Microsoft.Extensions.AI;
using Norvia.Agents.Runner;

namespace Norvia.Agents.UnitTests;

/// <summary>Tools the runner can call, answered from the test, with a record of the context the run opened them with.</summary>
internal sealed class FakeToolSource(IReadOnlyDictionary<string, Func<string>> answers) : IToolSource
{
    public ToolContext? Opened { get; private set; }

    public bool Disposed { get; private set; }

    public Task<IToolSession> OpenAsync(ToolContext context, CancellationToken cancellationToken)
    {
        Opened = context;
        return Task.FromResult<IToolSession>(new Session(this, [.. answers.Select(answer => Function(answer.Key, answer.Value))]));
    }

    private static AIFunction Function(string name, Func<string> answer) =>
        AIFunctionFactory.Create(
            (string? correlationId = null, string? logicApp = null, string? workflow = null, string? runId = null) => answer(),
            new AIFunctionFactoryOptions { Name = name, Description = name });

    private sealed class Session(FakeToolSource source, IReadOnlyList<AIFunction> tools) : IToolSession
    {
        public IReadOnlyList<AIFunction> Tools => tools;

        public ValueTask DisposeAsync()
        {
            source.Disposed = true;
            return ValueTask.CompletedTask;
        }
    }
}
