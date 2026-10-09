using Ardalis.SharedKernel;
using Mediator;
using Norvia.Tms.Domain.Fleet;

namespace Norvia.Tms.Application.Fleet.Planning.ListAbsences;

public sealed class ListAbsencesHandler(IReadRepository<Absence> absences, IDriverDirectory drivers, PlanningHorizonRules horizon, TimeProvider time)
    : IQueryHandler<ListAbsencesQuery, IReadOnlyList<AbsenceView>>
{
    public async ValueTask<IReadOnlyList<AbsenceView>> Handle(ListAbsencesQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var names = drivers.ListActive().ToDictionary(driver => driver.Id, driver => driver.Name);

        return
        [
            .. (await absences.ListAsync(new AbsencesFromSpec(horizon.Today(time.GetUtcNow())), cancellationToken))
                .Where(absence => names.ContainsKey(absence.DriverId))
                .Select(absence => new AbsenceView(absence.Id.Value, absence.DriverId.Value, names[absence.DriverId], absence.Date, absence.Reason, absence.Remark))
                .OrderBy(absence => absence.Date).ThenBy(absence => absence.DriverName, StringComparer.Ordinal),
        ];
    }
}
