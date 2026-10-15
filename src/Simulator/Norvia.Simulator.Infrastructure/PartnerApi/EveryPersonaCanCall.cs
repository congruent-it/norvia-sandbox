using Microsoft.Extensions.Options;
using Norvia.Simulator.Application.Ports;

namespace Norvia.Simulator.Infrastructure.PartnerApi;

/// <summary>Fails at startup when a Persona the Simulator books or plays a Carrier as has no client ID or subscription key to call Norvia with.</summary>
internal sealed class EveryPersonaCanCall(IWorldSource world) : IValidateOptions<PartnerApiOptions>
{
    public ValidateOptionsResult Validate(string? name, PartnerApiOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var failures = new List<string>();
        var loaded = world.Load();
        var personaIds = loaded.Shippers.Select(shipper => shipper.PersonaId)
            .Concat((loaded.Carriers ?? []).Where(carrier => carrier.Active && carrier.HasPersona).Select(carrier => carrier.PersonaId!));
        foreach (var personaId in personaIds)
        {
            try
            {
                options.ClientIdOf(personaId);
                options.SubscriptionKeyOf(personaId);
            }
            catch (InvalidOperationException missing)
            {
                failures.Add(missing.Message);
            }
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
