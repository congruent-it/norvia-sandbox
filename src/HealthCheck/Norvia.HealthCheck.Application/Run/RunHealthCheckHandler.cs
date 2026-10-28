using Ardalis.Result;
using Mediator;
using Norvia.HealthCheck.Application.Checks;
using Norvia.HealthCheck.Application.Estate;

namespace Norvia.HealthCheck.Application.Run;

public sealed class RunHealthCheckHandler(IEstateConfiguration estate, Checklist checklist, TimeProvider time) : IQueryHandler<RunHealthCheckQuery, Result<HealthCheckReport>>
{
    public async ValueTask<Result<HealthCheckReport>> Handle(RunHealthCheckQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var facts = await estate.ReadAsync(cancellationToken, query.Progress);
        query.Progress?.Report($"Running the {checklist.Checks.Count} Checks over what was read");
        return new HealthCheckReport(facts.Name, time.GetUtcNow(), checklist.Checks.Count, checklist.Run(facts));
    }
}
