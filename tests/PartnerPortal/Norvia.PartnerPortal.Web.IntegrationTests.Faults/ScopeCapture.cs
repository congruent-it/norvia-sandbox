using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace Norvia.PartnerPortal.Web.IntegrationTests.Faults;

/// <summary>Keeps what the portal logs with the keys of its logging scopes (coding guidelines §7), so a test reads a hop's outcome, failure code and fault as the Message trail would.</summary>
public sealed class ScopeCapture : ILoggerProvider, ISupportExternalScope
{
    private readonly ConcurrentQueue<IReadOnlyDictionary<string, object?>> _records = new();
    private IExternalScopeProvider? _scopes;

    /// <summary>The keys of every log record so far, the keys of all the scopes it was written in together.</summary>
    public IReadOnlyList<IReadOnlyDictionary<string, object?>> Records => [.. _records];

    public void SetScopeProvider(IExternalScopeProvider scopeProvider) => _scopes = scopeProvider;

    public ILogger CreateLogger(string categoryName) => new CapturingLogger(this);

    public void Dispose()
    {
    }

    private sealed class CapturingLogger(ScopeCapture capture) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            var keys = new Dictionary<string, object?>();
            capture._scopes?.ForEachScope(
                (scope, collected) =>
                {
                    if (scope is IEnumerable<KeyValuePair<string, object?>> pairs)
                    {
                        foreach (var (key, value) in pairs)
                        {
                            collected[key] = value;
                        }
                    }
                },
                keys);
            capture._records.Enqueue(keys);
        }
    }
}
