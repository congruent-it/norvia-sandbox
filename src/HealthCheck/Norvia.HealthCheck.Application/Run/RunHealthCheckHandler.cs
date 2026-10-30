using Ardalis.Result;
using Mediator;
using Norvia.HealthCheck.Application.Checks;
using Norvia.HealthCheck.Application.Estate;

namespace Norvia.HealthCheck.Application.Run;

public sealed class RunHealthCheckHandler(IEstateConfiguration estate, Checklist checklist, RecentReport recent, TimeProvider time) : IQueryHandler<RunHealthCheckQuery, Result<HealthCheckReport>>
{
    public async ValueTask<Result<HealthCheckReport>> Handle(RunHealthCheckQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (query.ReuseWithin is not { } maxAge)
        {
            return await RunAsync(query, cancellationToken);
        }

        await recent.Gate.WaitAsync(cancellationToken);
        try
        {
            return recent.Last is { } last && time.GetUtcNow() - last.RanAt <= maxAge
                ? last
                : await RunAsync(query, cancellationToken);
        }
        finally
        {
            recent.Gate.Release();
        }
    }

    private async Task<HealthCheckReport> RunAsync(RunHealthCheckQuery query, CancellationToken cancellationToken)
    {
        var facts = await estate.ReadAsync(cancellationToken, query.Progress);
        query.Progress?.Report($"Running the {checklist.Checks.Count} Checks over what was read");
        var report = new HealthCheckReport(facts.Name, time.GetUtcNow(), checklist.Checks.Count, checklist.Run(facts));
        recent.Last = report;
        return report;
    }
}
