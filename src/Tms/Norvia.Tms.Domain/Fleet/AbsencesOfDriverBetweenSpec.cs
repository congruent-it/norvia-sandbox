using Ardalis.Specification;

namespace Norvia.Tms.Domain.Fleet;

/// <summary>A Driver's Absences between two dates, to change in place.</summary>
public sealed class AbsencesOfDriverBetweenSpec : Specification<Absence>
{
    public AbsencesOfDriverBetweenSpec(DriverId driverId, DateOnly from, DateOnly to)
    {
        Query.Where(absence => absence.DriverId == driverId && absence.Date >= from && absence.Date <= to);
    }
}
