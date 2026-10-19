using Mediator;
using Norvia.PartnerPortal.Domain.Personas;

namespace Norvia.PartnerPortal.Application.CarrierInvoices.Draft;

/// <summary>
/// The Carrier Persona's draft Carrier invoice, built from its own records: its Confirmed bookings it reported Delivered
/// and hasn't invoiced yet, or whose invoiced line Norvia rejected. Needs no call to Norvia. Null when there is no such
/// Carrier Persona.
/// </summary>
public sealed record GetCarrierInvoiceDraftQuery(PersonaId PersonaId) : IQuery<CarrierInvoiceDraftView?>;
