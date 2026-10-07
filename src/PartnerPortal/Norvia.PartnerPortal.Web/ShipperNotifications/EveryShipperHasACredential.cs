using Microsoft.Extensions.Options;
using Norvia.PartnerPortal.Application.Personas;
using Norvia.PartnerPortal.Domain.Personas;

namespace Norvia.PartnerPortal.Web.ShipperNotifications;

/// <summary>Fails at startup when a Shipper Persona in the seed file has no credential for its notification endpoint.</summary>
internal sealed class EveryShipperHasACredential(IPersonaDirectory personas) : IValidateOptions<ShipperEndpointOptions>
{
    public ValidateOptionsResult Validate(string? name, ShipperEndpointOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var failures = EndpointCredential.Missing(personas, PartyType.Shipper, ShipperEndpointOptions.SectionName, options.ApiKeys).ToList();
        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
