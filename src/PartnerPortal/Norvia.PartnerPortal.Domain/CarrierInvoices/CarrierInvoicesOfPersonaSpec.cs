using Ardalis.Specification;
using Norvia.PartnerPortal.Domain.Personas;

namespace Norvia.PartnerPortal.Domain.CarrierInvoices;

/// <summary>The Carrier invoices a Carrier Persona sent, the latest first.</summary>
public sealed class CarrierInvoicesOfPersonaSpec : Specification<CarrierInvoice>
{
    public CarrierInvoicesOfPersonaSpec(PersonaId personaId)
    {
        Query.Where(invoice => invoice.PersonaId == personaId)
            .OrderByDescending(invoice => invoice.SubmittedAt)
            .AsNoTracking();
    }
}
