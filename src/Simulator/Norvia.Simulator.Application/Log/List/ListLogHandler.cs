using Ardalis.SharedKernel;
using Mediator;
using Norvia.Simulator.Domain.Log;

namespace Norvia.Simulator.Application.Log.List;

public sealed class ListLogHandler(IReadRepository<LogEntry> log) : IQueryHandler<ListLogQuery, IReadOnlyList<LogLine>>
{
    public async ValueTask<IReadOnlyList<LogLine>> Handle(ListLogQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var entries = await log.ListAsync(new LogEntriesOfRunSpec(query.RunId, query.Filter), cancellationToken);
        return [.. entries.Select(LogLine.From)];
    }
}
