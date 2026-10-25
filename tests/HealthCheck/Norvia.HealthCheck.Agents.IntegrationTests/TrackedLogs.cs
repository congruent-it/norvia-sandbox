using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace Norvia.HealthCheck.Agents.IntegrationTests;

/// <summary>
/// What the Aspire dashboard (or Application Insights) would hold of the two applications: every log record written in a scope that carries a
/// correlation ID, with the tracking contract's keys of its scopes. Both hosts write to the one store, so a trail reads as it does in the estate.
/// </summary>
internal sealed class TrackedStore
{
    private readonly ConcurrentQueue<TrackedHop> _recorded = new();

    public ILoggerProvider ProviderFor(string application) => new Provider(this, application);

    /// <summary>The hops on one trail, in the order they were written.</summary>
    public IReadOnlyList<TrackedHop> Trail(string correlationId) => [.. _recorded.Where(hop => hop.CorrelationId == correlationId)];

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
            if (keys.TryGetValue("correlationId", out var correlationId))
            {
                store._recorded.Enqueue(new TrackedHop(
                    application,
                    correlationId,
                    keys.GetValueOrDefault("flow"),
                    keys.GetValueOrDefault("agentId"),
                    keys.GetValueOrDefault("toolName"),
                    keys.GetValueOrDefault("outcome"),
                    keys.GetValueOrDefault("failureCode")));
            }
        }
    }
}

/// <summary>A hop of a Message trail as the tracking contract's keys give it.</summary>
internal sealed record TrackedHop(string Application, string CorrelationId, string? Flow, string? AgentId, string? ToolName, string? Outcome, string? FailureCode);
