using Mediator;

namespace Norvia.Tms.Application.Fleet.Planning.OpenDays;

/// <summary>
/// The daily horizon job (P10): opens every day of the Planning horizon that is missing, from the Rosters, and closes the
/// days before today. Running it again changes nothing.
/// </summary>
public sealed record OpenPlanningDaysCommand : ICommand<OpenPlanningDaysResult>;
