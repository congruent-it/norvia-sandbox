using Ardalis.Specification;
using Norvia.PartnerPortal.Domain.Personas;

namespace Norvia.PartnerPortal.Domain.ShipperInvoices;

/// <summary>The document a Shipper Persona already has for a document number.</summary>
public sealed class ShipperInvoiceByDocumentNumberSpec : Specification<ShipperInvoice>
{
    public ShipperInvoiceByDocumentNumberSpec(PersonaId personaId, string documentNumber)
    {
        Query.Where(invoice => invoice.PersonaId == personaId && invoice.DocumentNumber == documentNumber)
            .AsNoTracking();
    }
}
