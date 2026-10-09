using Norvia.Tms.Domain.Fleet;

namespace Norvia.Tms.Application.Fleet;

/// <summary>The Drivers' Rosters, which the TMS owns (P10). Until the Planner can edit them they come from the seed file.</summary>
public interface IRosterDirectory
{
    /// <summary>The Roster of every Driver who has one.</summary>
    IReadOnlyList<Roster> List();
}
