using System.ComponentModel.DataAnnotations;

namespace Norvia.Simulator.Infrastructure.Personas;

/// <summary>The profiles Carriers write in, in the Simulator's own configuration and not the seed (ADR 0002): the profiles, and the profile of each Carrier by its ID.</summary>
public sealed class PersonaOptions
{
    public const string SectionName = "Personas";

    public List<PersonaProfileOptions> Profiles { get; set; } = [];

    /// <summary>The profile ID of each Carrier, by Carrier ID.</summary>
    public Dictionary<string, string> Carriers { get; set; } = [];

    /// <summary>The profile ID of each Driver of the Own fleet, by Driver ID.</summary>
    public Dictionary<string, string> Drivers { get; set; } = [];

    public bool IsConsistent =>
        Carriers.Values.Concat(Drivers.Values).All(profile => Profiles.Exists(candidate => candidate.Id == profile)) && Profiles.Select(profile => profile.Id).Distinct().Count() == Profiles.Count;
}

public sealed class PersonaProfileOptions
{
    [Required]
    public string Id { get; set; } = string.Empty;

    /// <summary>The language the pools are kept under: <c>en</c>, <c>nl</c>, <c>de</c> or <c>mixed</c>.</summary>
    [Required]
    public string Language { get; set; } = string.Empty;

    [Required]
    public string Style { get; set; } = string.Empty;
}
