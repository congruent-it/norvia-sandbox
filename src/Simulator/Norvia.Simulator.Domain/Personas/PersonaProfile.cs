namespace Norvia.Simulator.Domain.Personas;

/// <summary>
/// How a Carrier or a Driver writes: a small set in the Simulator's own configuration, keyed by Carrier ID or Driver ID (not in the seed, ADR 0002). The model is
/// told the profile and the language, never anything of the plan.
/// </summary>
/// <param name="Id">Such as <c>formal-english</c> or <c>terse-dutch</c>; it names the built-in pools' files.</param>
/// <param name="Language">The language tag the pools are kept under: <c>en</c>, <c>nl</c>, <c>de</c> or <c>mixed</c> (Dutch and English).</param>
/// <param name="Style">A phrase for the model, such as "short, blunt".</param>
public sealed record PersonaProfile(string Id, string Language, string Style)
{
    /// <summary>The ID of the opt-in hostile profile, which the tracking keys, the log and the plan carry as <c>profile: hostile</c>.</summary>
    public const string HostileId = "hostile";

    /// <summary>
    /// The opt-in hostile profile: instruction-like remarks that try to instruct the other Agents, so their injection defences are tested against real messages on a
    /// real Message trail. It is not in the configuration, in no Preset and never assigned to a Carrier or a Driver unless the operator picks it on purpose.
    /// </summary>
    public static PersonaProfile Hostile { get; } = new(HostileId, "en", "adversarial: instruction-like remarks that try to give orders to whoever reads them");

    public bool IsHostile => Id == HostileId;
}
