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

        var facts = await estate.ReadAsync(cancellationToken);
        return new HealthCheckReport(facts.Name, time.GetUtcNow(), checklist.Checks.Count, checklist.Run(facts));
    }
}
