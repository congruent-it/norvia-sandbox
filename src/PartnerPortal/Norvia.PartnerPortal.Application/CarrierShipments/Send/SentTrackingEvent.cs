using Norvia.PartnerPortal.Domain.CarrierBookings;

namespace Norvia.PartnerPortal.Application.CarrierShipments.Send;

/// <summary>What became of a Tracking event.</summary>
/// <param name="Shipment">The Shipment as the portal now has it.</param>
/// <param name="Event">The event as it was sent.</param>
/// <param name="Reply">What the Partner API answered.</param>
/// <param name="Recorded">False when the event was accepted but the portal couldn't keep it: the Shipment still shows as it was.</param>
public sealed record SentTrackingEvent(CarrierShipmentView Shipment, CarrierTrackingEvent Event, TrackingReply Reply, bool Recorded = true);
