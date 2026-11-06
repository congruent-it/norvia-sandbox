using Norvia.Simulator.Domain.Personas;

namespace Norvia.Simulator.Application.Ports;

/// <summary>The curated pools kept in the repo, one per profile and category, which are the default and the fallback.</summary>
public interface IBuiltInPools
{
    /// <summary>The texts of the profile's built-in pool for a category; none when the repo holds no such pool.</summary>
    IReadOnlyList<string> TextsOf(PersonaProfile profile, TextCategory category);
}
