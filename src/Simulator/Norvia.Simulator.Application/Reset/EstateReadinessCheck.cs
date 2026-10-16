using Ardalis.Specification;
using Norvia.Simulator.Application.Ports;
using Norvia.Simulator.Domain.Runs;

namespace Norvia.Simulator.Application.Reset;

/// <summary>Asks whether a Simulation run is active and each application whether it would take a reset. Quiet comes first: nothing is deleted before it is known.</summary>
internal static class EstateReadinessCheck
{
    public static async Task<EstateResetReadiness> RunAsync(IReadRepositoryBase<SimulationRun> runs, IEstateReset doors, CancellationToken cancellationToken)
    {
        var active = await runs.AnyAsync(new ActiveRunSpec(), cancellationToken);
        var apps = await Task.WhenAll(EstateApps.All.Select(app => doors.CheckAsync(app, cancellationToken)));
        return new EstateResetReadiness(active, apps);
    }
}
