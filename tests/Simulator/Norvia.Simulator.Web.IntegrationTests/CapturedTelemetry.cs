using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using Microsoft.Extensions.Logging;

namespace Norvia.Simulator.Web.IntegrationTests;

/// <summary>
/// What the Simulator in the tests' process logged and traced, as text: each log entry's message, its scope and its exception, and each span's name and tags.
/// A test reads it to see which tracking keys went out with a call, and that no file content did.
/// </summary>
public sealed class CapturedTelemetry : ILoggerProvider, ISupportExternalScope, IDisposable
{
    private readonly ConcurrentQueue<string> _entries = new();
    private readonly ActivityListener _listener;
    private IExternalScopeProvider _scopes = new LoggerExternalScopeProvider();

    public CapturedTelemetry()
    {
        _listener = new ActivityListener
        {
            ShouldListenTo = _ => true,
            Sample = (ref _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity => _entries.Enqueue(Describe(activity)),
        };
        ActivitySource.AddActivityListener(_listener);
    }

    /// <summary>Every entry so far, one string each.</summary>
    public IReadOnlyList<string> Entries => [.. _entries];

    public ILogger CreateLogger(string categoryName) => new Capture(this, categoryName);

    public void SetScopeProvider(IExternalScopeProvider scopeProvider) => _scopes = scopeProvider;

    public void Dispose() => _listener.Dispose();

    private static string Describe(Activity activity) =>
        $"{activity.DisplayName} {string.Join(' ', activity.TagObjects.Select(tag => string.Create(CultureInfo.InvariantCulture, $"{tag.Key}={tag.Value}")))}";

    private sealed class Capture(CapturedTelemetry owner, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => owner._scopes.Push(state);

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            ArgumentNullException.ThrowIfNull(formatter);
            var scopes = new List<string>();
            owner._scopes.ForEachScope(
                (scope, into) =>
                {
                    if (scope is IEnumerable<KeyValuePair<string, object?>> pairs)
                    {
                        into.AddRange(pairs.Select(pair => string.Create(CultureInfo.InvariantCulture, $"{pair.Key}={pair.Value}")));
                    }
                },
                scopes);
            owner._entries.Enqueue($"{category}: {formatter(state, exception)} {string.Join(' ', scopes)} {exception}");
        }
    }
}
