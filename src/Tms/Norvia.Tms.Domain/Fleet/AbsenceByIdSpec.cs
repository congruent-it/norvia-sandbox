using Ardalis.Specification;

namespace Norvia.Tms.Domain.Fleet;

public sealed class AbsenceByIdSpec : Specification<Absence>
{
    public AbsenceByIdSpec(AbsenceId id)
    {
        Query.Where(absence => absence.Id == id);
    }
}
