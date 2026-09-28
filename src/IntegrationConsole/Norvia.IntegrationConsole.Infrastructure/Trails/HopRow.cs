using Azure.Monitor.Query.Logs.Models;
using Norvia.IntegrationConsole.Application.Trails;

namespace Norvia.IntegrationConsole.Infrastructure.Trails;

/// <summary>One row of the <see cref="HopQuery"/>, as its kind says: a call, a Logic App run's row, a record or a link.</summary>
internal sealed record HopRow(
    string Kind,
    string? Id,
    string? ParentId,
    DateTimeOffset? Start,
    double? DurationMs,
    string? Name,
    string? Path,
    string? Role,
    string? RunId,
    string? Workflow,
    string? Category,
    string? Flow,
    string? Outcome,
    string? FailureCode,
    string? EventType,
    IReadOnlyList<BusinessKey> BusinessKeys,
    bool InProcess = false)
{
    public const string Request = "request";
    public const string Run = "run";
    public const string Record = "record";
    public const string Link = "link";

    /// <summary>A Logic App run's row for its trigger, whose ID and parent are the run's.</summary>
    public bool IsTrigger => Category == "Workflow.Operations.Triggers";

    public DateTimeOffset? End => Start is { } start ? start + TimeSpan.FromMilliseconds(DurationMs ?? 0) : null;

    public static HopRow From(LogsTableRow row) => new(
        row.GetString("rowKind"),
        Text(row, "id"),
        Text(row, "parentId"),
        row.GetDateTimeOffset("start"),
        row.GetDouble("durationMs"),
        Text(row, "name"),
        Text(row, "path"),
        Text(row, "role"),
        Text(row, "runId"),
        Text(row, "workflow"),
        Text(row, "category"),
        Text(row, "flow"),
        Text(row, "outcome"),
        Text(row, "failureCode"),
        Text(row, "eventType"),
        [.. TrackedBusinessKeys.All.Where(key => Text(row, key.Key) is not null).Select(key => new BusinessKey(key.Kind, Text(row, key.Key)!))],
        row.GetBoolean("inProcess") ?? false);

    // A key that doesn't apply comes back empty from the query, and is left out as the tracking contract leaves it out.
    private static string? Text(LogsTableRow row, string column) => row.GetString(column) is { Length: > 0 } text ? text : null;
}
