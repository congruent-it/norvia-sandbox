using Ardalis.Specification;
using Norvia.PartnerPortal.Domain.Personas;

namespace Norvia.PartnerPortal.Domain.CarrierInvoices;

/// <summary>The outcome a Carrier Persona already has for an invoice number.</summary>
public sealed class CarrierInvoiceOutcomeByNumberSpec : Specification<CarrierInvoiceOutcome>
{
    public CarrierInvoiceOutcomeByNumberSpec(PersonaId personaId, string invoiceNumber)
    {
        Query.Where(outcome => outcome.PersonaId == personaId && outcome.InvoiceNumber == invoiceNumber)
            .AsNoTracking();
    }
}
