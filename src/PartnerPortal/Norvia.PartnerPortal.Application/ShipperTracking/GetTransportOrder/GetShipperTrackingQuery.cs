using Mediator;
using Norvia.PartnerPortal.Domain.Personas;

namespace Norvia.PartnerPortal.Application.ShipperTracking.GetTransportOrder;

/// <summary>One of the Shipper Persona's Transport Orders by Norvia's number, with its progress and current Proof of delivery.</summary>
public sealed record GetShipperTrackingQuery(PersonaId PersonaId, string TransportOrderNumber) : IQuery<TrackingAnswer<ShipperTrackingDetail>>;
