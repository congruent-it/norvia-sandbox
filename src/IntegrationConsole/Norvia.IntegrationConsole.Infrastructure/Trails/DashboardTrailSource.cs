using System.Globalization;
using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Norvia.IntegrationConsole.Application.Runs;
using Norvia.IntegrationConsole.Application.Trails;

namespace Norvia.IntegrationConsole.Infrastructure.Trails;

/// <summary>
/// Searches the telemetry the Aspire dashboard holds for Message trails, when the console runs under the local AppHost
/// (ADR 0027). Locally the applications log the tracking contract's keys (coding guidelines §7) in their logging scope,
/// which reaches the dashboard as each log record's attributes. It answers the same search as
/// <see cref="LogAnalyticsTrailSource"/>, and reads those attributes only, never a record's body. A trail's hops are
/// the requests those records were written in, from the spans of their traces. With no Logic Apps locally, it also
/// lists the failures those records tell of as failed runs (<see cref="IFailedRuns"/>).
/// </summary>
internal sealed class DashboardTrailSource(HttpClient http, IOptions<DashboardTrailSourceOptions> options) : ITrailSource, IFailedRuns
{
    /// <summary>
    /// The dashboard keeps at most this many log records in memory (<c>Dashboard:TelemetryLimits:MaxLogCount</c>), so
    /// asking for all of them reads everything it still holds.
    /// </summary>
    internal const int MaximumLogRecords = 10_000;

    /// <summary>The API key header of the dashboard's telemetry API.</summary>
    internal const string ApiKeyHeader = "x-api-key";

    // OTLP's span kinds of a call coming in: a request served, or a message received.
    private const int ServerSpan = 2;
    private const int ConsumerSpan = 5;

    public async Task<IReadOnlyList<FoundTrail>> SearchAsync(TrailSearch search, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(search);

        var records = (await TrackedRecordsAsync(cancellationToken))
            .Where(record => record.Time >= search.From && record.Time <= search.To)
            .ToList();

        // First the trails that carried the key, then everything recorded under their correlation IDs.
        var found = records.Where(record => record.Carries(search.Key))
            .Select(record => record.CorrelationId)
            .ToHashSet(StringComparer.Ordinal);

        return
        [
            .. records.Where(record => found.Contains(record.CorrelationId))
                .GroupBy(record => record.CorrelationId, StringComparer.Ordinal)
                .Select(Trail)
                .OrderByDescending(trail => trail.LastSeen)
                .Take(FoundTrails.Maximum),
        ];
    }

    public async Task<IReadOnlyList<RecordedHop>> HopsAsync(TrailLookup lookup, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(lookup);

        // The console's own audit events are read by AuditAsync, and say what became of a hop's message; they aren't hops.
        var records = (await TrackedRecordsAsync(cancellationToken))
            .Where(record => !record.IsAudit && record.Time >= lookup.From && record.Time <= lookup.To
                && string.Equals(record.CorrelationId, lookup.CorrelationId, StringComparison.OrdinalIgnoreCase))
            .ToList();

        var spans = new Dictionary<string, Span>(StringComparer.Ordinal);
        foreach (var traceId in records.Select(record => record.TraceId).Where(id => id.Length > 0).Distinct(StringComparer.Ordinal))
        {
            foreach (var span in await SpansAsync(traceId, cancellationToken))
            {
                spans[span.SpanId] = span;
            }
        }

        // A record written outside any span is a hop of its own. A published event is a hop of the span it was published
        // in, which continues the trace of what raised the event, so it is caused by that, not a part of it.
        var hops = records.Select((record, index) => (Record: record, HopId: record.SpanId.Length > 0
                ? record.Keys.ContainsKey("eventType") && spans.ContainsKey(record.SpanId) ? record.SpanId : EntryOf(record.SpanId, spans)
                : $"{record.Resource}#{index}"))
            .GroupBy(record => record.HopId, record => record.Record, StringComparer.Ordinal)
            .ToList();
        var hopIds = hops.Select(hop => hop.Key).ToHashSet(StringComparer.Ordinal);

        return
        [
            .. hops.Select(hop => Hop(
                hop.Key,
                [.. hop.OrderBy(record => record.Time)],
                spans.GetValueOrDefault(hop.Key),
                spans.GetValueOrDefault(hop.Key) is { } span
                    ? HopCauses.Of(hop.Key, span.ParentSpanId, hopIds, id => spans.GetValueOrDefault(id)?.ParentSpanId)
                    : null)),
        ];
    }

    public async Task<IReadOnlyList<RecordedAudit>> AuditAsync(TrailLookup lookup, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(lookup);

        return
        [
            .. (await TrackedRecordsAsync(cancellationToken))
                .Where(record => record.IsAudit && record.Time >= lookup.From && record.Time <= lookup.To
                    && string.Equals(record.CorrelationId, lookup.CorrelationId, StringComparison.OrdinalIgnoreCase))
                .Select(record => new RecordedAudit(
                    record.Keys["Actor"],
                    record.Keys["Action"],
                    record.Keys.GetValueOrDefault("Entity") ?? string.Empty,
                    record.Keys.GetValueOrDefault("MessageId") ?? string.Empty,
                    record.Time,
                    record.Keys.GetValueOrDefault("Reason"),
                    BusinessKeysOf([record]))),
        ];
    }

    /// <summary>
    /// A failed run locally is where the tracking keys say <c>outcome=Failed</c>: one per correlation ID, flow and
    /// application, as of its last such record. There are no steps to name, so the failure code is the error, and no run to open.
    /// </summary>
    public async Task<IReadOnlyList<RecordedFailedRun>> FailedRunsAsync(DateTimeOffset since, CancellationToken cancellationToken)
    {
        var records = await TrackedRecordsAsync(cancellationToken);
        return
        [
            .. records.Where(record => record.Time >= since && record.Keys.GetValueOrDefault("outcome") == nameof(Outcome.Failed))
                .GroupBy(record => (record.CorrelationId, Flow: record.Keys.GetValueOrDefault("flow"), record.Resource))
                .Select(failures => failures.MaxBy(record => record.Time)!)
                .Select(record => new RecordedFailedRun(
                    null,
                    record.Time,
                    record.Keys.GetValueOrDefault("flow"),
                    record.CorrelationId,
                    null,
                    record.Keys.GetValueOrDefault("failureCode") is { } code ? new StepError(code, null) : null)),
        ];
    }

    private static FoundTrail Trail(IGrouping<string, TrackedRecord> hops) => new(
        hops.Key,
        Distinct(hops, "flow"),
        hops.Min(hop => hop.Time),
        hops.Max(hop => hop.Time),
        [.. Distinct(hops, "outcome").Select(FoundTrails.OutcomeOf).OfType<Outcome>()],
        BusinessKeysOf(hops));

    private static RecordedHop Hop(string id, List<TrackedRecord> records, Span? span, string? causedBy)
    {
        var (outcome, failureCode) = HopOutcomes.Last(records.Select(record => ((DateTimeOffset?)record.Time, record.Keys.GetValueOrDefault("outcome"), record.Keys.GetValueOrDefault("failureCode"))));
        return new RecordedHop(
            id,
            causedBy,
            span is null || Distinct(records, "eventType").Length > 0
                ? HopNames.OfRecords(records[0].Resource, Distinct(records, "eventType").FirstOrDefault())
                : HopNames.OfCall(span.Resource, span.Name, span.Method, span.Path),
            Distinct(records, "flow").FirstOrDefault(),
            span?.Start ?? records[0].Time,
            span is { End: { } end } ? end - span.Start : null,
            outcome,
            failureCode,
            BusinessKeysOf(records),
            MessageId: Distinct(records, "messageId").FirstOrDefault(),
            Fault: Distinct(records, "fault").FirstOrDefault());
    }

    /// <summary>
    /// The request a span belongs to: the nearest span above it that served a call or received a message, within the
    /// same application. Logs written during a database call in a request are the request's. With no such span, as for
    /// a worker or a Blazor event, it is the topmost span the application started itself, which names what it did,
    /// rather than a framework's, such as Blazor's for the event; or the topmost span when the application started none.
    /// </summary>
    private static string EntryOf(string spanId, Dictionary<string, Span> spans)
    {
        if (!spans.TryGetValue(spanId, out var span))
        {
            return spanId;
        }

        var own = span.IsFramework ? null : span;
        var seen = new HashSet<string>(StringComparer.Ordinal) { span.SpanId };
        while (span.Kind is not (ServerSpan or ConsumerSpan)
            && span.ParentSpanId is { } parentId
            && spans.TryGetValue(parentId, out var parent)
            && parent.Resource == span.Resource
            && seen.Add(parent.SpanId))
        {
            span = parent;
            own = span.IsFramework ? own : span;
        }

        return span.Kind is ServerSpan or ConsumerSpan ? span.SpanId : (own ?? span).SpanId;
    }

    private static BusinessKey[] BusinessKeysOf(IEnumerable<TrackedRecord> records) =>
        [.. TrackedBusinessKeys.All.SelectMany(key => Distinct(records, key.Key).Select(value => new BusinessKey(key.Kind, value)))];

    private static string[] Distinct(IEnumerable<TrackedRecord> hops, string key) =>
        [.. hops.Select(hop => hop.Keys.GetValueOrDefault(key)).OfType<string>().Distinct(StringComparer.Ordinal)];

    private async Task<List<TrackedRecord>> TrackedRecordsAsync(CancellationToken cancellationToken)
    {
        using var logs = await GetAsync($"api/telemetry/logs?limit={MaximumLogRecords.ToString(CultureInfo.InvariantCulture)}", missingIsEmpty: false, cancellationToken);

        // OTLP JSON: data.resourceLogs[].scopeLogs[].logRecords[], a scope being the logging category. An application's
        // own records only, not those a framework wrote in its logging scope.
        return
        [
            .. ItemsOf(DataOf(logs), "resourceLogs")
                .SelectMany(resource => ItemsOf(resource, "scopeLogs")
                    .Where(scope => !FrameworkLogs.Wrote(CategoryOf(scope)))
                    .SelectMany(scope => ItemsOf(scope, "logRecords"))
                    .Select(record => TrackedRecord.From(record, ServiceOf(resource))))
                .OfType<TrackedRecord>(),
        ];
    }

    private static string? CategoryOf(JsonElement scope) =>
        scope.TryGetProperty("scope", out var named) && named.ValueKind == JsonValueKind.Object
            && named.TryGetProperty("name", out var name) && name.ValueKind == JsonValueKind.String
            ? name.GetString()
            : null;

    /// <summary>Every span of a trace, or none once the dashboard no longer holds it.</summary>
    private async Task<List<Span>> SpansAsync(string traceId, CancellationToken cancellationToken)
    {
        using var trace = await GetAsync($"api/telemetry/traces/{Uri.EscapeDataString(traceId)}", missingIsEmpty: true, cancellationToken);

        // OTLP JSON: data.resourceSpans[].scopeSpans[].spans[].
        return trace is null
            ? []
            :
            [
                .. ItemsOf(DataOf(trace), "resourceSpans")
                    .SelectMany(resource => ItemsOf(resource, "scopeSpans")
                        .SelectMany(scope => ItemsOf(scope, "spans").Select(span => Span.From(span, ServiceOf(resource), CategoryOf(scope)))))
                    .OfType<Span>(),
            ];
    }

    /// <summary>The telemetry API's answer, or null when <paramref name="missingIsEmpty"/> and it holds nothing at that address.</summary>
    private async Task<JsonDocument?> GetAsync(string path, bool missingIsEmpty, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(path, UriKind.Relative));
        if (options.Value.ApiKey is { Length: > 0 } apiKey)
        {
            request.Headers.Add(ApiKeyHeader, apiKey);
        }

        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (missingIsEmpty && response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        await using var content = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await JsonDocument.ParseAsync(content, cancellationToken: cancellationToken);
    }

    private static JsonElement DataOf(JsonDocument? answer) =>
        answer is not null && answer.RootElement.ValueKind == JsonValueKind.Object && answer.RootElement.TryGetProperty("data", out var data) ? data : default;

    private static JsonElement[] ItemsOf(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(property, out var array) && array.ValueKind == JsonValueKind.Array
            ? [.. array.EnumerateArray()]
            : [];

    private static string ServiceOf(JsonElement resource) =>
        AttributeOf(resource.ValueKind == JsonValueKind.Object && resource.TryGetProperty("resource", out var attributes) ? attributes : default, "service.name")
            ?? "unknown";

    private static string? AttributeOf(JsonElement element, string key) =>
        ItemsOf(element, "attributes")
            .Where(attribute => attribute.TryGetProperty("key", out var name) && name.GetString() == key)
            .Select(attribute => attribute.TryGetProperty("value", out var value) ? TextOf(value) : null)
            .FirstOrDefault();

    private static string? TextOf(JsonElement value) =>
        value.TryGetProperty("stringValue", out var text) ? text.GetString()
        : value.TryGetProperty("intValue", out var number) ? number.ToString()
        : null;

    private static string StringOf(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";

    // OTLP JSON writes a 64-bit integer as a string, as the dashboard does; a number is taken too.
    private static DateTimeOffset? TimeOf(JsonElement element, string property) =>
        element.TryGetProperty(property, out var time)
        && (time.ValueKind == JsonValueKind.String
            ? ulong.TryParse(time.GetString(), NumberStyles.None, CultureInfo.InvariantCulture, out var nanoseconds)
            : time.ValueKind == JsonValueKind.Number && time.TryGetUInt64(out nanoseconds))
        && nanoseconds > 0
            ? DateTimeOffset.UnixEpoch.AddTicks((long)(nanoseconds / 100))
            : null;

    /// <summary>
    /// A span reduced to what places and names a hop: its parent, its application, its name, the method and path a
    /// request called, its times, and whether a framework started it rather than the application.
    /// </summary>
    private sealed record Span(
        string SpanId, string? ParentSpanId, string Resource, string Name, string? Method, string? Path, int Kind, DateTimeOffset Start, DateTimeOffset? End, bool IsFramework)
    {
        // A span's scope is the activity source that started it, and the frameworks name theirs as their logging categories.
        public static Span? From(JsonElement span, string resource, string? scope) =>
            StringOf(span, "spanId") is { Length: > 0 } spanId && TimeOf(span, "startTimeUnixNano") is { } start
                ? new Span(
                    spanId,
                    StringOf(span, "parentSpanId") is { Length: > 0 } parent ? parent : null,
                    resource,
                    StringOf(span, "name"),
                    AttributeOf(span, "http.request.method"),
                    AttributeOf(span, "url.path"),
                    span.TryGetProperty("kind", out var kind) && kind.ValueKind == JsonValueKind.Number ? kind.GetInt32() : 0,
                    start,
                    TimeOf(span, "endTimeUnixNano"),
                    FrameworkLogs.Wrote(scope))
                : null;
    }

    /// <summary>A log record reduced to its time, where it was written and the tracking contract's keys, when it carries a correlation ID.</summary>
    private sealed record TrackedRecord(DateTimeOffset Time, string CorrelationId, string Resource, string TraceId, string SpanId, Dictionary<string, string> Keys)
    {
        // The attributes the console's audit event is logged with (OpenTelemetryAuditLog), named after its message template's placeholders.
        private static readonly string[] AuditKeys = ["Actor", "Action", "Entity", "MessageId", "Reason"];

        private static readonly string[] TrackedKeys =
            ["correlationId", "flow", "outcome", "failureCode", "fault", "eventType", "messageId", .. AuditKeys, .. TrackedBusinessKeys.All.Select(key => key.Key)];

        public bool IsAudit => Keys.ContainsKey("Actor") && Keys.ContainsKey("Action");

        public bool Carries(string key) =>
            string.Equals(CorrelationId, key, StringComparison.OrdinalIgnoreCase)
            || TrackedBusinessKeys.All.Any(business => string.Equals(Keys.GetValueOrDefault(business.Key), key, StringComparison.OrdinalIgnoreCase));

        // Only the attributes are read: the body is the formatted message, which is no key.
        public static TrackedRecord? From(JsonElement record, string resource)
        {
            var keys = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var attribute in ItemsOf(record, "attributes"))
            {
                if (attribute.TryGetProperty("key", out var name) && name.GetString() is { } key && TrackedKeys.Contains(key)
                    && attribute.TryGetProperty("value", out var value) && TextOf(value) is { Length: > 0 } text)
                {
                    keys[key] = text;
                }
            }

            return keys.TryGetValue("correlationId", out var correlationId) && TimeOf(record, "timeUnixNano") is { } time
                ? new TrackedRecord(time, correlationId, resource, StringOf(record, "traceId"), StringOf(record, "spanId"), keys)
                : null;
        }
    }
}
