using Mediator;
using Norvia.PartnerPortal.Domain.Personas;

namespace Norvia.PartnerPortal.Application.ShipperTracking.FindTransportOrder;

/// <summary>One of the Shipper Persona's Transport Orders by the Shipper's own reference.</summary>
public sealed record FindShipperTrackingQuery(PersonaId PersonaId, string ShipperReference) : IQuery<TrackingAnswer<ShipperTrackingDetail>>;
