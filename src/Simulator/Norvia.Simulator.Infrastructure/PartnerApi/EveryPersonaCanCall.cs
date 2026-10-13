using Microsoft.Extensions.Options;
using Norvia.Simulator.Application.Ports;

namespace Norvia.Simulator.Infrastructure.PartnerApi;

/// <summary>Fails at startup when a Persona the Simulator books as has no client ID or subscription key to call Norvia with.</summary>
internal sealed class EveryPersonaCanCall(IWorldSource world) : IValidateOptions<PartnerApiOptions>
{
    public ValidateOptionsResult Validate(string? name, PartnerApiOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var failures = new List<string>();
        foreach (var shipper in world.Load().Shippers)
        {
            try
            {
                options.ClientIdOf(shipper.PersonaId);
                options.SubscriptionKeyOf(shipper.PersonaId);
            }
            catch (InvalidOperationException missing)
            {
                failures.Add(missing.Message);
            }
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
