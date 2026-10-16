using Ardalis.SharedKernel;
using Mediator;
using Norvia.Simulator.Application.Ports;
using Norvia.Simulator.Domain.Runs;

namespace Norvia.Simulator.Application.Reset;

public sealed class GetResetReadinessHandler(IReadRepository<SimulationRun> runs, IEstateReset doors)
    : IQueryHandler<GetResetReadinessQuery, EstateResetReadiness>
{
    public async ValueTask<EstateResetReadiness> Handle(GetResetReadinessQuery query, CancellationToken cancellationToken) =>
        await EstateReadinessCheck.RunAsync(runs, doors, cancellationToken);
}
