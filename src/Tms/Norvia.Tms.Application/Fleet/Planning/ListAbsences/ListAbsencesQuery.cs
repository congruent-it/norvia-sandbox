using Mediator;

namespace Norvia.Tms.Application.Fleet.Planning.ListAbsences;

/// <summary>The Absences of today and later, earliest first, for the Planner's Drivers tab.</summary>
public sealed record ListAbsencesQuery : IQuery<IReadOnlyList<AbsenceView>>;
