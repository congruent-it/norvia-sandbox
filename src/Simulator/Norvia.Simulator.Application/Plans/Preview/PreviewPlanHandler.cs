using Ardalis.Result;
using Ardalis.SharedKernel;
using Mediator;
using Norvia.Simulator.Application.Ports;
using Norvia.Simulator.Domain.Plans;
using Norvia.Simulator.Domain.Runs;

namespace Norvia.Simulator.Application.Plans.Preview;

public sealed class PreviewPlanHandler(IReadRepository<RunSequence> sequences, IWorldSource world, SimulationLimits limits)
    : IQueryHandler<PreviewPlanQuery, Result<PlanPreview>>
{
    public async ValueTask<Result<PlanPreview>> Handle(PreviewPlanQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var problems = query.Settings.Problems(limits);
        if (problems.Count > 0)
        {
            return Result.Invalid([.. problems.Select(problem => new ValidationError(problem))]);
        }

        var next = (await sequences.FirstOrDefaultAsync(new RunSequenceSpec(), cancellationToken))?.Next ?? 1;
        return PlanPreview.From(BookingPlanGenerator.Generate(query.Settings, world.Load(), next));
    }
}
