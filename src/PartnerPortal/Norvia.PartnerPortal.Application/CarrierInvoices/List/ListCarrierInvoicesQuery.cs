using Mediator;
using Norvia.PartnerPortal.Domain.Personas;

namespace Norvia.PartnerPortal.Application.CarrierInvoices.List;

/// <summary>The Carrier invoices a Carrier Persona sent, the latest first, each with the outcome per line once Norvia told it. An invoice the Persona sent another way is listed by its outcome alone.</summary>
public sealed record ListCarrierInvoicesQuery(PersonaId PersonaId) : IQuery<IReadOnlyList<CarrierInvoiceView>>;
