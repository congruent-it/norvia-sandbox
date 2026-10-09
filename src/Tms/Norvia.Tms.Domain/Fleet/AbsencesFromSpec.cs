using Ardalis.Specification;

namespace Norvia.Tms.Domain.Fleet;

/// <summary>The Absences from a date on, in order, read-only: what the Drivers tab lists, and what the horizon job honours.</summary>
public sealed class AbsencesFromSpec : Specification<Absence>
{
    public AbsencesFromSpec(DateOnly from)
    {
        Query.Where(absence => absence.Date >= from).OrderBy(absence => absence.Date).AsNoTracking();
    }
}
