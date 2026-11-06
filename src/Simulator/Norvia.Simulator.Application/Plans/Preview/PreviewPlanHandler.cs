using Ardalis.Result;
using Ardalis.SharedKernel;
using Mediator;
using Norvia.Simulator.Application.Plans.Pools;
using Norvia.Simulator.Application.Ports;
using Norvia.Simulator.Domain.Plans;
using Norvia.Simulator.Domain.Runs;

namespace Norvia.Simulator.Application.Plans.Preview;

public sealed class PreviewPlanHandler(IReadRepository<RunSequence> sequences, IWorldSource world, SimulationLimits limits, PlanPools pools)
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

        if (!world.Load().Shippers.Any(shipper => shipper.Lanes.Count > 0))
        {
            return Result.Invalid(new ValidationError("No Shipper has a Rate Card with a Lane to book for."));
        }

        var next = RunSequence.NextNumber(await sequences.FirstOrDefaultAsync(new RunSequenceSpec(), cancellationToken));
        return PlanPreview.From(await pools.AddToAsync(BookingPlanGenerator.Generate(query.Settings, world.Load(), next), world.Load(), cancellationToken));
    }
}
