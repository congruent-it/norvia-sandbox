using Microsoft.Extensions.Options;
using Norvia.Simulator.Application.Ports;
using Norvia.Simulator.Domain.Personas;

namespace Norvia.Simulator.Infrastructure.Personas;

/// <summary>The profiles from the Simulator's configuration.</summary>
internal sealed class ConfiguredPersonaProfiles(IOptions<PersonaOptions> options) : IPersonaProfiles
{
    public PersonaProfile? ProfileOf(string carrierId)
    {
        var settings = options.Value;
        return settings.Carriers.TryGetValue(carrierId, out var profileId) && settings.Profiles.Find(profile => profile.Id == profileId) is { } profile
            ? new PersonaProfile(profile.Id, profile.Language, profile.Style)
            : null;
    }
}
