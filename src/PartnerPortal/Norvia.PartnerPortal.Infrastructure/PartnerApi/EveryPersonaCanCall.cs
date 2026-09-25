using Microsoft.Extensions.Options;
using Norvia.PartnerPortal.Application.Personas;

namespace Norvia.PartnerPortal.Infrastructure.PartnerApi;

/// <summary>Fails at startup when a Persona in the seed file has no client ID or subscription key to call Norvia with.</summary>
internal sealed class EveryPersonaCanCall(IPersonaDirectory personas) : IValidateOptions<PartnerApiOptions>
{
    public ValidateOptionsResult Validate(string? name, PartnerApiOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var failures = new List<string>();
        foreach (var persona in personas.All)
        {
            try
            {
                options.ClientIdOf(persona.Id);
                options.SubscriptionKeyOf(persona.Id);
            }
            catch (InvalidOperationException missing)
            {
                failures.Add(missing.Message);
            }
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
