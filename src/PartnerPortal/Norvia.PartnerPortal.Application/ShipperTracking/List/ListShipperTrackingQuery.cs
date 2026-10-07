using Mediator;

namespace Norvia.PartnerPortal.Application.ShipperTracking.List;

/// <summary>The Shipper Persona's Transport Orders, as Norvia answers them through the Partner API, newest collection first.</summary>
public sealed record ListShipperTrackingQuery(string PersonaId, ShipperTrackingFilter Filter) : IQuery<TrackingAnswer<IReadOnlyList<ShipperTrackingSummary>>>;
