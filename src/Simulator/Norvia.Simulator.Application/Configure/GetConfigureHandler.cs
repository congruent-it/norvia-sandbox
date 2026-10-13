using Ardalis.SharedKernel;
using Mediator;
using Norvia.Simulator.Application.Ports;
using Norvia.Simulator.Domain.Runs;

namespace Norvia.Simulator.Application.Configure;

public sealed class GetConfigureHandler(IReadRepository<RunSequence> sequences, ISeedSource seeds, SimulationLimits limits)
    : IQueryHandler<GetConfigureQuery, ConfigureView>
{
    public async ValueTask<ConfigureView> Handle(GetConfigureQuery query, CancellationToken cancellationToken)
    {
        var next = (await sequences.FirstOrDefaultAsync(new RunSequenceSpec(), cancellationToken))?.Next ?? 1;
        return new ConfigureView(Presets.All, limits, seeds.NewSeed(), next);
    }
}
