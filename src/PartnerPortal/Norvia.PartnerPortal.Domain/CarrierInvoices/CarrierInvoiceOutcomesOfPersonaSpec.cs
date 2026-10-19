using Ardalis.Specification;
using Norvia.PartnerPortal.Domain.Personas;

namespace Norvia.PartnerPortal.Domain.CarrierInvoices;

/// <summary>The outcomes Norvia told a Carrier Persona.</summary>
public sealed class CarrierInvoiceOutcomesOfPersonaSpec : Specification<CarrierInvoiceOutcome>
{
    public CarrierInvoiceOutcomesOfPersonaSpec(PersonaId personaId)
    {
        Query.Where(outcome => outcome.PersonaId == personaId)
            .AsNoTracking();
    }
}
