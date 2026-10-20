using Mediator;
using Norvia.PartnerPortal.Domain.CarrierInvoices;
using Norvia.PartnerPortal.Domain.Personas;

namespace Norvia.PartnerPortal.Application.CarrierInvoices.Outcomes;

/// <summary>The outcomes Norvia told a Carrier Persona, whether or not the Persona sent the invoice from the portal.</summary>
public sealed record ListCarrierInvoiceOutcomesQuery(PersonaId PersonaId) : IQuery<IReadOnlyList<CarrierInvoiceOutcome>>;
