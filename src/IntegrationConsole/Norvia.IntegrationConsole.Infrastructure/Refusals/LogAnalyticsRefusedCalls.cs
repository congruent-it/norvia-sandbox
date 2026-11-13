using Azure.Monitor.Query.Logs;
using Microsoft.Extensions.Options;
using Norvia.IntegrationConsole.Application.Refusals;
using Norvia.IntegrationConsole.Infrastructure.Trails;

namespace Norvia.IntegrationConsole.Infrastructure.Refusals;

/// <summary>
/// Lists the calls API Management refused at the edge from the Log Analytics workspace behind the estate's Application
/// Insights, as the console's own identity (ADR 0017). It reads keys only, never a body, a token or a subscription key.
/// </summary>
internal sealed class LogAnalyticsRefusedCalls(LogsQueryClient client, IOptions<TrailSourceOptions> options, TimeProvider time) : IRefusedCalls
{
    public async Task<IReadOnlyList<RecordedRefusal>> RefusalsAsync(DateTimeOffset since, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(options.Value.LogAnalyticsWorkspaceId))
        {
            throw new InvalidOperationException(
                $"The console isn't connected to an estate: {TrailSourceOptions.SectionName}:{nameof(TrailSourceOptions.LogAnalyticsWorkspaceId)} is not set.");
        }

        // Refuse a partial answer (the client's default), which could leave out the very refusals the operator looks for.
        var response = await client.QueryWorkspaceAsync(
            options.Value.LogAnalyticsWorkspaceId,
            RefusalQuery.Text,
            new LogsQueryTimeRange(since, time.GetUtcNow()),
            cancellationToken: cancellationToken);

        return
        [
            .. response.Value.Table.Rows.Select(row => new RecordedRefusal(
                row.GetDateTimeOffset("time") ?? default,
                row.GetString("operation") ?? RefusalStatus.Unknown,
                row.GetInt32("statusCode") ?? 0,
                row.GetString("failureCode") ?? RefusalQuery.SubscriptionKeyRejected,
                row.GetString("caller") is { Length: > 0 } caller ? caller : null,
                row.GetString("correlationId") is { Length: > 0 } correlationId ? correlationId : null)),
        ];
    }
}
