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
        var next = RunSequence.NextNumber(await sequences.FirstOrDefaultAsync(new RunSequenceSpec(), cancellationToken));
        return new ConfigureView(Presets.All, limits, seeds.NewSeed(), next);
    }
}
