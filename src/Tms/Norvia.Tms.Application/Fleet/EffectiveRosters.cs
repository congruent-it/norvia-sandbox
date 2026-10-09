using Ardalis.SharedKernel;
using Norvia.Tms.Domain.Fleet;

namespace Norvia.Tms.Application.Fleet;

/// <summary>
/// The Rosters the TMS works with (P10): the ones the Planner changed, and for every other Driver the starting Roster from
/// the seed file.
/// </summary>
public sealed class EffectiveRosters(IRosterDirectory seeded, IReadRepository<Roster> changed)
{
    public async ValueTask<IReadOnlyList<Roster>> ListAsync(CancellationToken cancellationToken)
    {
        var overrides = (await changed.ListAsync(cancellationToken)).ToDictionary(roster => roster.DriverId);
        return [.. seeded.List().Where(roster => !overrides.ContainsKey(roster.DriverId)), .. overrides.Values];
    }

    public async ValueTask<Roster?> FindAsync(DriverId driverId, CancellationToken cancellationToken)
    {
        if (await changed.FirstOrDefaultAsync(new RosterByDriverSpec(driverId), cancellationToken) is { } roster)
        {
            return roster;
        }

        return seeded.List().FirstOrDefault(roster => roster.DriverId == driverId);
    }
}
