using Microsoft.Extensions.Options;
using Norvia.PartnerPortal.Application.Personas;
using Norvia.PartnerPortal.Domain.Personas;

namespace Norvia.PartnerPortal.Web.CarrierBookings;

/// <summary>Fails at startup when a Carrier Persona in the seed file has no credential for its booking endpoint.</summary>
internal sealed class EveryCarrierHasACredential(IPersonaDirectory personas) : IValidateOptions<CarrierEndpointOptions>
{
    public ValidateOptionsResult Validate(string? name, CarrierEndpointOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var failures = EndpointCredential.Missing(personas, PartyType.Carrier, CarrierEndpointOptions.SectionName, options.ApiKeys).ToList();
        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
