using Ardalis.Result;
using Mediator;
using Norvia.PartnerPortal.Domain.CarrierBookings;

namespace Norvia.PartnerPortal.Application.CarrierShipments.Send;

/// <summary>
/// Sends a Tracking event for one of a Carrier Persona's Shipments to Norvia, through the Partner API, with a unique event ID
/// and a location taken from the Shipment's addresses. Not found when the Persona has no such Carrier Persona or Shipment;
/// Invalid when the event isn't one the Shipment may have next, or its addresses can't give a location.
/// </summary>
public sealed record SendTrackingCommand(string PersonaId, string CarrierBookingNumber, TrackingEventType Type)
    : ICommand<Result<SentTrackingEvent>>;
