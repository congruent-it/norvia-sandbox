using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Norvia.IntegrationConsole.Application.Runs;
using Norvia.IntegrationConsole.Application.Trails;

namespace Norvia.IntegrationConsole.Agents.IntegrationTests;

/// <summary>
/// What the Aspire dashboard (or Application Insights) would hold of the two applications: every log record written in a scope that carries a
/// correlation ID, with the tracking contract's keys of its scopes. Both hosts write to the one store, so a trail reads as it does in the estate.
/// </summary>
internal sealed class TrackedStore : ITrailSource
{
    private readonly ConcurrentQueue<RecordedHop> _recorded = new();
    private readonly ConcurrentDictionary<string, List<RecordedHop>> _seeded = new(StringComparer.Ordinal);
    private readonly ConcurrentQueue<LoggedAudit> _audits = new();
    private int _next;

    /// <summary>The audit events the console wrote, with the keys of the scope each was written in, as the pipeline would export them.</summary>
    public IReadOnlyList<LoggedAudit> Audits => [.. _audits];

    /// <summary>Puts hops on a trail as if earlier applications had recorded them.</summary>
    public void Seed(string correlationId, params RecordedHop[] hops) => _seeded.AddOrUpdate(correlationId, _ => [.. hops], (_, existing) => [.. existing, .. hops]);

    public ILoggerProvider ProviderFor(string application) => new Provider(this, application);

    public Task<IReadOnlyList<FoundTrail>> SearchAsync(TrailSearch search, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<FoundTrail>>([]);

    public Task<IReadOnlyList<RecordedHop>> HopsAsync(TrailLookup lookup, CancellationToken cancellationToken)
    {
        var seeded = _seeded.GetValueOrDefault(lookup.CorrelationId) ?? [];
        var logged = _recorded.Where(hop => hop.Id.StartsWith(lookup.CorrelationId + "|", StringComparison.Ordinal));
        return Task.FromResult<IReadOnlyList<RecordedHop>>([.. seeded.Concat(logged)]);
    }

    public Task<IReadOnlyList<RecordedAudit>> AuditAsync(TrailLookup lookup, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<RecordedAudit>>([]);

    private void Record(string application, Dictionary<string, string> keys, DateTimeOffset time)
    {
        if (!keys.TryGetValue("correlationId", out var correlationId))
        {
            return;
        }

        _recorded.Enqueue(new RecordedHop(
            $"{correlationId}|{application}#{Interlocked.Increment(ref _next)}",
            null,
            $"{application} {keys.GetValueOrDefault("flow")}",
            keys.GetValueOrDefault("flow"),
            time,
            null,
            Enum.TryParse<Outcome>(keys.GetValueOrDefault("outcome"), out var outcome) ? outcome : null,
            keys.GetValueOrDefault("failureCode"),
            [],
            AgentId: keys.GetValueOrDefault("agentId"),
            ToolName: keys.GetValueOrDefault("toolName")));
    }

    private sealed class Provider(TrackedStore store, string application) : ILoggerProvider, ISupportExternalScope
    {
        private IExternalScopeProvider _scopes = new LoggerExternalScopeProvider();

        public ILogger CreateLogger(string categoryName) => new TrackingLogger(store, application, categoryName, () => _scopes);

        public void SetScopeProvider(IExternalScopeProvider scopeProvider) => _scopes = scopeProvider;

        public void Dispose()
        {
        }
    }

    private sealed class TrackingLogger(TrackedStore store, string application, string category, Func<IExternalScopeProvider> scopes) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => scopes().Push(state);

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            // The framework's own logs (the HTTP client's, say) join the scope but aren't the application's hops.
            if (category.StartsWith("System.", StringComparison.Ordinal) || category.StartsWith("Microsoft.", StringComparison.Ordinal))
            {
                return;
            }

            var keys = new Dictionary<string, string>(StringComparer.Ordinal);
            // Outer scopes first, so an inner scope's outcome is the one that stands.
            scopes().ForEachScope(
                (scope, into) =>
                {
                    if (scope is IEnumerable<KeyValuePair<string, object?>> pairs)
                    {
                        foreach (var (key, value) in pairs)
                        {
                            if (value is not null)
                            {
                                into[key] = value.ToString()!;
                            }
                        }
                    }
                },
                keys);
            if (category.EndsWith(".OpenTelemetryAuditLog", StringComparison.Ordinal))
            {
                store._audits.Enqueue(new LoggedAudit(keys, formatter(state, exception)));
            }

            store.Record(application, keys, DateTimeOffset.UtcNow);
        }
    }
}

/// <summary>An audit event as logged: the message, and the tracking contract's keys and the agent's of its scope.</summary>
internal sealed record LoggedAudit(IReadOnlyDictionary<string, string> Keys, string Message);

/// <summary>The Logic Apps management API, holding the runs a test gives it, and noting whether anything asked for a step's bodies.</summary>
internal sealed class StubRuns : IWorkflowRuns
{
    private readonly ConcurrentDictionary<WorkflowRun, RecordedRun> _runs = new();

    public bool BodiesAsked { get; private set; }

    public void Keep(WorkflowRun run, RecordedRun recorded) => _runs[run] = recorded;

    public Task<RecordedRun?> RunAsync(WorkflowRun run, CancellationToken cancellationToken) => Task.FromResult(_runs.GetValueOrDefault(run));

    public Task<RecordedBodies?> BodiesAsync(WorkflowRun run, string stepName, CancellationToken cancellationToken)
    {
        BodiesAsked = true;
        return Task.FromResult<RecordedBodies?>(null);
    }
}

/// <summary>Where the console lists failed runs from, holding the runs a test gives it.</summary>
internal sealed class StubFailedRuns : IFailedRuns
{
    private readonly ConcurrentQueue<RecordedFailedRun> _runs = new();

    public void Add(RecordedFailedRun run) => _runs.Enqueue(run);

    public Task<IReadOnlyList<RecordedFailedRun>> FailedRunsAsync(DateTimeOffset since, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<RecordedFailedRun>>([.. _runs]);
}
