using Microsoft.Extensions.Options;
using Norvia.Simulator.Application.Ports;
using Norvia.Simulator.Domain.Personas;

namespace Norvia.Simulator.Infrastructure.Personas;

/// <summary>The profiles from the Simulator's configuration.</summary>
internal sealed class ConfiguredPersonaProfiles(IOptions<PersonaOptions> options) : IPersonaProfiles
{
    public PersonaProfile? ProfileOf(string carrierId) => Find(options.Value.Carriers, carrierId);

    public PersonaProfile? ProfileOfDriver(string driverId) => Find(options.Value.Drivers, driverId);

    private PersonaProfile? Find(Dictionary<string, string> byId, string id) =>
        byId.TryGetValue(id, out var profileId) && options.Value.Profiles.Find(profile => profile.Id == profileId) is { } profile
            ? new PersonaProfile(profile.Id, profile.Language, profile.Style)
            : null;
}
