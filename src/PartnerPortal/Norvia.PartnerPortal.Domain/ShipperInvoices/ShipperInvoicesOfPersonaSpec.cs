using Ardalis.Specification;
using Norvia.PartnerPortal.Domain.Personas;

namespace Norvia.PartnerPortal.Domain.ShipperInvoices;

/// <summary>The invoices and Credit notes in a Shipper Persona's inbox, the latest first.</summary>
public sealed class ShipperInvoicesOfPersonaSpec : Specification<ShipperInvoice>
{
    public ShipperInvoicesOfPersonaSpec(PersonaId personaId)
    {
        Query.Where(invoice => invoice.PersonaId == personaId)
            .OrderByDescending(invoice => invoice.ReceivedAt)
            .AsNoTracking();
    }
}
