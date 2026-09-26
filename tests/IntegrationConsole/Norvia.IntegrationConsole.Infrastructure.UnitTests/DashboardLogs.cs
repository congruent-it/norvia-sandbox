using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json.Nodes;

namespace Norvia.IntegrationConsole.Infrastructure.UnitTests;

/// <summary>
/// Stands in for the Aspire dashboard's telemetry API: answers <c>/api/telemetry/logs</c> with the log records the test
/// adds, in the OTLP JSON the dashboard returns, and keeps the requests it got.
/// </summary>
public sealed class DashboardLogs : HttpMessageHandler
{
    private readonly Dictionary<string, List<JsonObject>> _recordsByResource = [];

    public HttpStatusCode Status { get; set; } = HttpStatusCode.OK;

    public List<HttpRequestMessage> Requests { get; } = [];

    /// <summary>A log record of <paramref name="resource"/> at <paramref name="time"/>, with these logging-scope keys.</summary>
    public DashboardLogs Add(string resource, DateTimeOffset time, Dictionary<string, string> keys, string body = "A log message")
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

        records.Add(new JsonObject
        {
            // As the dashboard sends it: OTLP JSON writes a 64-bit integer as a string.
            ["timeUnixNano"] = ((ulong)(time - DateTimeOffset.UnixEpoch).Ticks * 100).ToString(CultureInfo.InvariantCulture),
            ["severityNumber"] = 9,
            ["severityText"] = "Information",
            ["body"] = new JsonObject { ["stringValue"] = body },
            ["attributes"] = attributes,
        });
        return this;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add(request);
        var resourceLogs = new JsonArray();
        foreach (var (resource, records) in _recordsByResource)
        {
            resourceLogs.Add(new JsonObject
            {
                ["resource"] = new JsonObject
                {
                    ["attributes"] = new JsonArray { new JsonObject { ["key"] = "service.name", ["value"] = new JsonObject { ["stringValue"] = resource } } },
                },
                ["scopeLogs"] = new JsonArray
                {
                    new JsonObject
                    {
                        ["scope"] = new JsonObject { ["name"] = "Norvia" },
                        ["logRecords"] = new JsonArray([.. records.Select(record => record.DeepClone())]),
                    },
                },
            });
        }

        var count = _recordsByResource.Values.Sum(records => records.Count);
        var answer = new JsonObject
        {
            ["data"] = new JsonObject { ["resourceLogs"] = resourceLogs },
            ["totalCount"] = count,
            ["returnedCount"] = count,
        };
        return Task.FromResult(new HttpResponseMessage(Status)
        {
            Content = new StringContent(answer.ToJsonString(), Encoding.UTF8, "application/json"),
        });
    }
}
