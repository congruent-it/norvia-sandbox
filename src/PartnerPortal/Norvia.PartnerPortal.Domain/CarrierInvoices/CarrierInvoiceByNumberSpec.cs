using Ardalis.Specification;
using Norvia.PartnerPortal.Domain.Personas;

namespace Norvia.PartnerPortal.Domain.CarrierInvoices;

/// <summary>The Carrier invoice a Carrier Persona already has for an invoice number.</summary>
public sealed class CarrierInvoiceByNumberSpec : Specification<CarrierInvoice>
{
    public CarrierInvoiceByNumberSpec(PersonaId personaId, string invoiceNumber)
    {
        Query.Where(invoice => invoice.PersonaId == personaId && invoice.InvoiceNumber == invoiceNumber)
            .AsNoTracking();
    }
}
