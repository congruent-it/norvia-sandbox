using Ardalis.Specification;

namespace Norvia.Tms.Domain.Fleet;

public sealed class RosterByDriverSpec : Specification<Roster>
{
    public RosterByDriverSpec(DriverId driverId)
    {
        Query.Where(roster => roster.DriverId == driverId).AsNoTracking();
    }
}
