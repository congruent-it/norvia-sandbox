namespace Norvia.Tms.Domain.TrackingEvents;

/// <summary>
/// What a reporter said about a Shipment (P5): who, under which event ID, about which Carrier booking, what happened,
/// when, where, and optionally the ETA and a remark. It travels as one value from the call to the Tracking event that
/// keeps it; the call's own correlation ID and when the TMS received it are not the reporter's words.
/// </summary>
/// <param name="Source">Who reported it.</param>
/// <param name="EventId">The reporter's own ID for the event, unique per source.</param>
/// <param name="CarrierBookingReference">The Carrier's own reference for the booking the event is about; null for an Own fleet event.</param>
/// <param name="CarrierBookingNumber">The TMS's own number for the Carrier booking, which finds the Shipment only when the reference doesn't.</param>
/// <param name="EventTime">When it happened, with the offset of where.</param>
/// <param name="OwnFleet">For an Own fleet report (P12), the Trip, Driver and Shipment it is about in place of a Carrier booking; null for a Carrier's.</param>
public sealed record TrackingReport(
    TrackingSource Source,
    string EventId,
    string? CarrierBookingReference,
    string? CarrierBookingNumber,
    TrackingEventType Type,
    DateTimeOffset EventTime,
    TrackingLocation Location,
    DateTimeOffset? Eta,
    string? Remark,
    OwnFleetReport? OwnFleet = null);
