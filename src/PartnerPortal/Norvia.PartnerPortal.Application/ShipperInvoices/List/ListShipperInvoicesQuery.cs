using Mediator;
using Norvia.PartnerPortal.Domain.Personas;

namespace Norvia.PartnerPortal.Application.ShipperInvoices.List;

/// <summary>The invoices and Credit notes in a Shipper Persona's inbox, the latest first.</summary>
public sealed record ListShipperInvoicesQuery(PersonaId PersonaId) : IQuery<IReadOnlyList<ShipperInvoiceView>>;
