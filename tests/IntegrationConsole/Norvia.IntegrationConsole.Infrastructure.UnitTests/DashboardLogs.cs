using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json.Nodes;

namespace Norvia.IntegrationConsole.Infrastructure.UnitTests;

/// <summary>
/// Stands in for the Aspire dashboard's telemetry API: answers <c>/api/telemetry/logs</c> with the log records the test
/// adds, and <c>/api/telemetry/traces/{traceId}</c> with the spans it adds, in the OTLP JSON the dashboard returns, and
/// keeps the requests it got.
/// </summary>
public sealed class DashboardLogs : HttpMessageHandler
{
    private const string TracesPath = "/api/telemetry/traces/";

    private readonly Dictionary<string, List<(string Category, JsonObject Record)>> _recordsByResource = [];
    private readonly List<(string Resource, string Scope, JsonObject Span)> _spans = [];

    public HttpStatusCode Status { get; set; } = HttpStatusCode.OK;

    public List<HttpRequestMessage> Requests { get; } = [];

    /// <summary>
    /// A log record of <paramref name="resource"/> at <paramref name="time"/>, with these logging-scope keys, written in
    /// the span <paramref name="spanId"/> of trace <paramref name="traceId"/>, or outside any span when they're left out,
    /// under the logging <paramref name="category"/>, which OTLP names the record's scope.
    /// </summary>
    public DashboardLogs Add(
        string resource,
        DateTimeOffset time,
        Dictionary<string, string> keys,
        string body = "A log message",
        string traceId = "",
        string spanId = "",
        string category = "Norvia.Tms.Api")
    {
        var attributes = new JsonArray();
        foreach (var (key, value) in keys)
        {
            attributes.Add(new JsonObject { ["key"] = key, ["value"] = new JsonObject { ["stringValue"] = value } });
        }

        if (!_recordsByResource.TryGetValue(resource, out var records))
        {
            _recordsByResource[resource] = records = [];
        }

        records.Add((category, new JsonObject
        {
            // As the dashboard sends it: OTLP JSON writes a 64-bit integer as a string.
            ["timeUnixNano"] = UnixNano(time),
            ["severityNumber"] = 9,
            ["severityText"] = "Information",
            ["body"] = new JsonObject { ["stringValue"] = body },
            ["attributes"] = attributes,
            ["traceId"] = traceId,
            ["spanId"] = spanId,
        }));
        return this;
    }

    /// <summary>
    /// A span of <paramref name="resource"/>; <paramref name="kind"/> is OTLP's, 2 for a server span, and
    /// <paramref name="attributes"/> its string attributes, such as a request's <c>url.path</c>. <paramref name="scope"/>
    /// is the activity source that started it, which OTLP names the span's scope.
    /// </summary>
    public DashboardLogs AddSpan(
        string resource,
        string traceId,
        string spanId,
        string? parentSpanId,
        string name,
        int kind,
        DateTimeOffset start,
        DateTimeOffset end,
        Dictionary<string, string>? attributes = null,
        string scope = "Microsoft.AspNetCore")
    {
        var spanAttributes = new JsonArray();
        foreach (var (key, value) in attributes ?? [])
        {
            spanAttributes.Add(new JsonObject { ["key"] = key, ["value"] = new JsonObject { ["stringValue"] = value } });
        }

        _spans.Add((resource, scope, new JsonObject
        {
            ["traceId"] = traceId,
            ["spanId"] = spanId,
            ["parentSpanId"] = parentSpanId,
            ["name"] = name,
            ["kind"] = kind,
            ["startTimeUnixNano"] = UnixNano(start),
            ["endTimeUnixNano"] = UnixNano(end),
            ["attributes"] = spanAttributes,
        }));
        return this;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add(request);
        var path = request.RequestUri!.AbsolutePath;
        if (!path.StartsWith(TracesPath, StringComparison.Ordinal))
        {
            return Answer(new JsonObject { ["resourceLogs"] = ResourceLogs() }, _recordsByResource.Values.Sum(records => records.Count));
        }

        var traceId = path[TracesPath.Length..];
        var spans = _spans.Where(span => (string?)span.Span["traceId"] == traceId).ToList();
        return spans.Count == 0
            ? Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound))
            : Answer(new JsonObject { ["resourceSpans"] = ResourceSpans(spans) }, spans.Count);
    }

    private JsonArray ResourceLogs()
    {
        var resourceLogs = new JsonArray();
        foreach (var (resource, records) in _recordsByResource)
        {
            resourceLogs.Add(new JsonObject
            {
                ["resource"] = Resource(resource),
                ["scopeLogs"] = new JsonArray(
                [
                    .. records.GroupBy(record => record.Category).Select(scope => new JsonObject
                    {
                        ["scope"] = new JsonObject { ["name"] = scope.Key },
                        ["logRecords"] = new JsonArray([.. scope.Select(record => record.Record.DeepClone())]),
                    }),
                ]),
            });
        }

        return resourceLogs;
    }

    private static JsonArray ResourceSpans(List<(string Resource, string Scope, JsonObject Span)> spans) =>
    [
        .. spans.GroupBy(span => span.Resource).Select(resource => new JsonObject
        {
            ["resource"] = Resource(resource.Key),
            ["scopeSpans"] = new JsonArray(
            [
                .. resource.GroupBy(span => span.Scope).Select(scope => new JsonObject
                {
                    ["scope"] = new JsonObject { ["name"] = scope.Key },
                    ["spans"] = new JsonArray([.. scope.Select(span => span.Span.DeepClone())]),
                }),
            ]),
        }),
    ];

    private Task<HttpResponseMessage> Answer(JsonObject data, int count)
    {
        var answer = new JsonObject { ["data"] = data, ["totalCount"] = count, ["returnedCount"] = count };
        return Task.FromResult(new HttpResponseMessage(Status)
        {
            Content = new StringContent(answer.ToJsonString(), Encoding.UTF8, "application/json"),
        });
    }

    private static JsonObject Resource(string name) => new()
    {
        ["attributes"] = new JsonArray { new JsonObject { ["key"] = "service.name", ["value"] = new JsonObject { ["stringValue"] = name } } },
    };

    private static string UnixNano(DateTimeOffset time) =>
        ((ulong)(time - DateTimeOffset.UnixEpoch).Ticks * 100).ToString(CultureInfo.InvariantCulture);
}
