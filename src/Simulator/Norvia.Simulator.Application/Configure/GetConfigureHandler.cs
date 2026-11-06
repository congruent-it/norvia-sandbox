using Ardalis.SharedKernel;
using Mediator;
using Norvia.Simulator.Application.Ports;
using Norvia.Simulator.Domain.Runs;

namespace Norvia.Simulator.Application.Configure;

public sealed class GetConfigureHandler(IReadRepository<RunSequence> sequences, ISeedSource seeds, SimulationLimits limits, IWorldSource world)
    : IQueryHandler<GetConfigureQuery, ConfigureView>
{
    public async ValueTask<ConfigureView> Handle(GetConfigureQuery query, CancellationToken cancellationToken)
    {
        var next = RunSequence.NextNumber(await sequences.FirstOrDefaultAsync(new RunSequenceSpec(), cancellationToken));
        var master = world.Load();
        return new ConfigureView(Presets.All, limits, seeds.NewSeed(), next)
        {
            HostileCarriers = [.. master.ActiveCarriers.Where(carrier => carrier.HasPersona).Select(carrier => new HostileChoice(carrier.Id, carrier.Name))],
            HostileDrivers = [.. master.OwnFleetDrivers.Select(driver => new HostileChoice(driver.Id, driver.Name))],
        };
    }
}
