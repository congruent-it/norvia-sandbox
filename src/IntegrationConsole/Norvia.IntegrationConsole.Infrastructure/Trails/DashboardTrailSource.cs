using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Norvia.IntegrationConsole.Application.Trails;

namespace Norvia.IntegrationConsole.Infrastructure.Trails;

/// <summary>
/// Searches the telemetry the Aspire dashboard holds for Message trails, when the console runs under the local AppHost
/// (ADR 0027). Locally the applications log the tracking contract's keys (coding guidelines §7) in their logging scope,
/// which reaches the dashboard as each log record's attributes. It answers the same search as
/// <see cref="LogAnalyticsTrailSource"/>, and reads those attributes only, never a record's body.
/// </summary>
internal sealed class DashboardTrailSource(HttpClient http, IOptions<DashboardTrailSourceOptions> options) : ITrailSource
{
    /// <summary>
    /// The dashboard keeps at most this many log records in memory (<c>Dashboard:TelemetryLimits:MaxLogCount</c>), so
    /// asking for all of them reads everything it still holds.
    /// </summary>
    internal const int MaximumLogRecords = 10_000;

    /// <summary>The API key header of the dashboard's telemetry API.</summary>
    internal const string ApiKeyHeader = "x-api-key";

    private static readonly (string Key, BusinessKeyKind Kind)[] BusinessKeys =
    [
        ("transportOrderNumber", BusinessKeyKind.TransportOrderNumber),
        ("shipmentNumber", BusinessKeyKind.ShipmentNumber),
        ("carrierBookingNumber", BusinessKeyKind.CarrierBookingNumber),
        ("tripNumber", BusinessKeyKind.TripNumber),
        ("sourceMessageId", BusinessKeyKind.SourceMessageId),
    ];

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

    private static FoundTrail Trail(IGrouping<string, TrackedRecord> hops) => new(
        hops.Key,
        Distinct(hops, "flow"),
        hops.Min(hop => hop.Time),
        hops.Max(hop => hop.Time),
        [.. Distinct(hops, "outcome").Select(FoundTrails.OutcomeOf).OfType<Outcome>()],
        [.. BusinessKeys.SelectMany(key => Distinct(hops, key.Key).Select(value => new BusinessKey(key.Kind, value)))]);

    private static string[] Distinct(IEnumerable<TrackedRecord> hops, string key) =>
        [.. hops.Select(hop => hop.Keys.GetValueOrDefault(key)).OfType<string>().Distinct(StringComparer.Ordinal)];

    private async Task<List<TrackedRecord>> TrackedRecordsAsync(CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            new Uri($"api/telemetry/logs?limit={MaximumLogRecords.ToString(CultureInfo.InvariantCulture)}", UriKind.Relative));
        if (options.Value.ApiKey is { Length: > 0 } apiKey)
        {
            request.Headers.Add(ApiKeyHeader, apiKey);
        }

        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var content = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var logs = await JsonDocument.ParseAsync(content, cancellationToken: cancellationToken);

        // OTLP JSON: data.resourceLogs[].scopeLogs[].logRecords[].
        return
        [
            .. ItemsOf(logs.RootElement.TryGetProperty("data", out var data) ? data : default, "resourceLogs")
                .SelectMany(resource => ItemsOf(resource, "scopeLogs"))
                .SelectMany(scope => ItemsOf(scope, "logRecords"))
                .Select(TrackedRecord.From)
                .OfType<TrackedRecord>(),
        ];
    }

    private static JsonElement[] ItemsOf(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(property, out var array) && array.ValueKind == JsonValueKind.Array
            ? [.. array.EnumerateArray()]
            : [];

    /// <summary>A log record reduced to its time and the tracking contract's keys, when it carries a correlation ID.</summary>
    private sealed record TrackedRecord(DateTimeOffset Time, string CorrelationId, Dictionary<string, string> Keys)
    {
        private static readonly string[] TrackedKeys =
            ["correlationId", "flow", "outcome", .. BusinessKeys.Select(key => key.Key)];

        public bool Carries(string key) =>
            string.Equals(CorrelationId, key, StringComparison.OrdinalIgnoreCase)
            || BusinessKeys.Any(business => string.Equals(Keys.GetValueOrDefault(business.Key), key, StringComparison.OrdinalIgnoreCase));

        // Only the attributes are read: the body is the formatted message, which is no key.
        public static TrackedRecord? From(JsonElement record)
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

            return keys.TryGetValue("correlationId", out var correlationId) && TimeOf(record) is { } time
                ? new TrackedRecord(time, correlationId, keys)
                : null;
        }

        // OTLP JSON writes a 64-bit integer as a string, as the dashboard does; a number is taken too.
        private static DateTimeOffset? TimeOf(JsonElement record) =>
            record.TryGetProperty("timeUnixNano", out var time)
            && (time.ValueKind == JsonValueKind.String
                ? ulong.TryParse(time.GetString(), NumberStyles.None, CultureInfo.InvariantCulture, out var nanoseconds)
                : time.TryGetUInt64(out nanoseconds))
                ? DateTimeOffset.UnixEpoch.AddTicks((long)(nanoseconds / 100))
                : null;

        private static string? TextOf(JsonElement value) =>
            value.TryGetProperty("stringValue", out var text) ? text.GetString()
            : value.TryGetProperty("intValue", out var number) ? number.ToString()
            : null;
    }
}
