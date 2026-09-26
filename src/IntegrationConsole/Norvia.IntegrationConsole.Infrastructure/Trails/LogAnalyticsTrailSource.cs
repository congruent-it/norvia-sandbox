using System.Text.Json;
using Azure.Monitor.Query.Logs;
using Azure.Monitor.Query.Logs.Models;
using Microsoft.Extensions.Options;
using Norvia.IntegrationConsole.Application.Trails;

namespace Norvia.IntegrationConsole.Infrastructure.Trails;

/// <summary>
/// Searches the Log Analytics workspace behind the estate's Application Insights for Message trails, as the console's
/// own identity (the client's credential), never with the operator's token (ADR 0017). It reads the tracking contract's
/// keys only (coding guidelines §7), so no message body is ever fetched.
/// </summary>
internal sealed class LogAnalyticsTrailSource(LogsQueryClient client, IOptions<TrailSourceOptions> options) : ITrailSource
{
    private static readonly (string Column, BusinessKeyKind Kind)[] BusinessKeyColumns =
    [
        ("transportOrderNumbers", BusinessKeyKind.TransportOrderNumber),
        ("shipmentNumbers", BusinessKeyKind.ShipmentNumber),
        ("carrierBookingNumbers", BusinessKeyKind.CarrierBookingNumber),
        ("tripNumbers", BusinessKeyKind.TripNumber),
        ("sourceMessageIds", BusinessKeyKind.SourceMessageId),
    ];

    public async Task<IReadOnlyList<FoundTrail>> SearchAsync(TrailSearch search, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(search);

        var workspaceId = options.Value.LogAnalyticsWorkspaceId;
        if (string.IsNullOrWhiteSpace(workspaceId))
        {
            throw new InvalidOperationException(
                $"The console isn't connected to an estate: {TrailSourceOptions.SectionName}:{nameof(TrailSourceOptions.LogAnalyticsWorkspaceId)} is not set.");
        }

        // The client's default refuses a partial answer (LogsQueryOptions.AllowPartialErrors), which could leave out the
        // very hop that failed.
        var response = await client.QueryWorkspaceAsync(
            workspaceId,
            TrailQuery.For(search.Key, FoundTrails.Maximum),
            new LogsQueryTimeRange(search.From, search.To),
            cancellationToken: cancellationToken);

        return [.. response.Value.Table.Rows.Select(Trail)];
    }

    private static FoundTrail Trail(LogsTableRow row) => new(
        row.GetString("correlationId"),
        Strings(row, "flows"),
        row.GetDateTimeOffset("firstSeen") ?? default,
        row.GetDateTimeOffset("lastSeen") ?? default,
        [.. Strings(row, "outcomes").Select(FoundTrails.OutcomeOf).OfType<Outcome>()],
        [.. BusinessKeyColumns.SelectMany(key => Strings(row, key.Column).Select(value => new BusinessKey(key.Kind, value)))]);

    // The query returns each set as a JSON array in a string column.
    private static string[] Strings(LogsTableRow row, string column) =>
        row.GetString(column) is { Length: > 0 } json ? JsonSerializer.Deserialize<string[]>(json) ?? [] : [];
}
