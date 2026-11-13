using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Azure.Core;
using Azure.Core.Pipeline;
using Azure.Monitor.Query.Logs;
using Norvia.IntegrationConsole.Infrastructure.Refusals;

namespace Norvia.IntegrationConsole.Infrastructure.IntegrationTests.Refusals;

/// <summary>
/// Stands in for the Log Analytics workspace behind the estate's Application Insights, at its HTTP API: the real
/// <see cref="LogsQueryClient"/> talks to it. It holds the request telemetry API Management sent and the traces its policies
/// wrote, and answers the console's refusal query as the workspace would (the join on the operation, the caller and the
/// correlation ID the query reads), within the time range the request carries. It keeps the requests it got.
/// </summary>
internal sealed class FakeWorkspace : HttpMessageHandler
{
    private readonly List<GatewayRequest> _requests = [];
    private readonly List<PolicyTrace> _traces = [];

    public List<(Uri Uri, string? Authorization, JsonNode Body)> Received { get; } = [];

    public FakeWorkspace Request(GatewayRequest request)
    {
        _requests.Add(request);
        return this;
    }

    public FakeWorkspace Trace(PolicyTrace trace)
    {
        _traces.Add(trace);
        return this;
    }

    /// <summary>A client of this workspace, which calls it as the credential says.</summary>
    public LogsQueryClient Client(TokenCredential credential) => new(
        credential,
        new LogsQueryClientOptions { Transport = new HttpClientTransport(new HttpClient(this)) });

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = JsonNode.Parse(await request.Content!.ReadAsStringAsync(cancellationToken))!;
        Received.Add((request.RequestUri!, request.Headers.Authorization?.ToString(), body));

        if ((string?)body["query"] != RefusalQuery.Text)
        {
            return new HttpResponseMessage(HttpStatusCode.BadRequest) { Content = new StringContent("{}") };
        }

        var (start, end) = Range((string)body["timespan"]!);
        var rows = new JsonArray();
        foreach (var gateway in _requests.Where(r => r.Time >= start && r.Time <= end && r.AppRoleName.StartsWith("apim", StringComparison.Ordinal)
            && RefusalStatusCodes.Contains(r.ResultCode)).OrderByDescending(r => r.Time))
        {
            var failureCode = _traces.FirstOrDefault(trace => trace.OperationId == gateway.OperationId)?.FailureCode
                ?? (gateway.ResultCode == "401" ? RefusalQuery.SubscriptionKeyRejected : gateway.ResultCode == "429" ? "RateLimitExceeded" : "Forbidden");
            rows.Add(new JsonArray(
                gateway.Time.UtcDateTime.ToString("O"),
                gateway.Name,
                int.Parse(gateway.ResultCode, System.Globalization.CultureInfo.InvariantCulture),
                failureCode,
                gateway.Properties.GetValueOrDefault("azp") ?? gateway.Properties.GetValueOrDefault("Subscription Name") ?? string.Empty,
                gateway.Properties.GetValueOrDefault("Response-X-Correlation-ID") ?? gateway.Properties.GetValueOrDefault("Request-X-Correlation-ID") ?? string.Empty));
        }

        var answer = new JsonObject
        {
            ["tables"] = new JsonArray(new JsonObject
            {
                ["name"] = "PrimaryResult",
                ["columns"] = new JsonArray(
                    Column("time", "datetime"), Column("operation", "string"), Column("statusCode", "int"),
                    Column("failureCode", "string"), Column("caller", "string"), Column("correlationId", "string")),
                ["rows"] = rows,
            }),
        };
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(answer.ToJsonString(), Encoding.UTF8, "application/json") };
    }

    private static readonly string[] RefusalStatusCodes = ["401", "403", "429"];

    private static JsonObject Column(string name, string type) => new() { ["name"] = name, ["type"] = type };

    // The timespan of a query is an ISO 8601 interval: start/end.
    private static (DateTimeOffset Start, DateTimeOffset End) Range(string timespan)
    {
        var parts = timespan.Split('/');
        return (DateTimeOffset.Parse(parts[0], System.Globalization.CultureInfo.InvariantCulture), DateTimeOffset.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture));
    }
}

/// <summary>A row of API Management's request telemetry (<c>AppRequests</c>), with the properties the console reads.</summary>
internal sealed record GatewayRequest(DateTimeOffset Time, string Name, string ResultCode, string OperationId, Dictionary<string, string> Properties, string AppRoleName = "apim-norvia");

/// <summary>A trace one of API Management's policies wrote (<c>AppTraces</c>) with outcome <c>Refused</c>.</summary>
internal sealed record PolicyTrace(string OperationId, string FailureCode);
