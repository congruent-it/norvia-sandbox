using Mediator;

namespace Norvia.IntegrationConsole.Application.Refusals.List;

public sealed class ListRefusedCallsHandler(IRefusedCalls refusals, TimeProvider time) : IQueryHandler<ListRefusedCallsQuery, RefusedCallList>
{
    private static readonly TimeSpan Window = TimeSpan.FromHours(24);

    public async ValueTask<RefusedCallList> Handle(ListRefusedCallsQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var to = time.GetUtcNow();
        var from = to - Window;
        var recorded = await refusals.RefusalsAsync(from, cancellationToken);

        var calls = recorded
            .Where(refusal => refusal.Time >= from)
            .OrderByDescending(refusal => refusal.Time)
            .Select(refusal => new RefusedCall(refusal.Time, refusal.Operation, refusal.StatusCode, refusal.FailureCode, refusal.Caller ?? RefusalStatus.Unknown, refusal.CorrelationId))
            .ToList();
        var operations = calls.Select(call => call.Operation).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();

        var operation = query.Operation?.Trim();
        var shown = calls
            .Where(call => query.Status is null || call.StatusCode == query.Status)
            .Where(call => string.IsNullOrEmpty(operation) || call.Operation == operation)
            .ToList();
        var groups = shown
            .GroupBy(call => (call.Caller, call.StatusCode))
            .Select(group => new RefusalGroup(group.Key.Caller, group.Key.StatusCode, group.Count()))
            .OrderByDescending(group => group.Count)
            .ThenBy(group => group.Caller, StringComparer.Ordinal)
            .ThenBy(group => group.StatusCode)
            .ToList();
        return new RefusedCallList(from, to, operations, groups, shown);
    }
}
