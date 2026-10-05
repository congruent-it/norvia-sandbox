namespace Norvia.PartnerPortal.Domain.CarrierBookings;

/// <summary>
/// One Tracking event a Carrier Persona sends for a Shipment: what happened, when, and where, the place being one of the
/// Shipment's own addresses. It never names the Carrier: Norvia takes it from the token (ADR 0005).
/// </summary>
/// <param name="EventId">The Carrier's own ID for the event, unique per Carrier.</param>
/// <param name="BookingReference">The Carrier's own reference for its booking.</param>
/// <param name="Country">The ISO 3166-1 alpha-3 country code.</param>
public sealed record CarrierTrackingEvent(
    string EventId,
    string BookingReference,
    string CarrierBookingNumber,
    TrackingEventType Type,
    DateTimeOffset EventTime,
    string Place,
    string Postcode,
    string Country);
