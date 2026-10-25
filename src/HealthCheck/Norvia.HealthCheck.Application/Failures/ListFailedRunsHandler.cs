using Ardalis.Result;
using Mediator;
using Norvia.HealthCheck.Application.Estate;

namespace Norvia.HealthCheck.Application.Failures;

public sealed class ListFailedRunsHandler(IEstateConfiguration estate) : IQueryHandler<ListFailedRunsQuery, Result<FailedRunsReport>>
{
    public async ValueTask<Result<FailedRunsReport>> Handle(ListFailedRunsQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var facts = await estate.ReadAsync(cancellationToken);
        return new FailedRunsReport(
            facts.Name,
            EstateConfiguration.FailureWindowDays,
            [
                .. facts.Flows
                    .Where(flow => flow.FailedRuns > 0)
                    .OrderByDescending(flow => flow.FailedRuns)
                    .ThenBy(flow => flow.Name, StringComparer.Ordinal)
                    .Select(flow => new FailedFlow(flow.Name, flow.FailedRuns!.Value, flow.HasFailedRunAlert)),
            ]);
    }
}
