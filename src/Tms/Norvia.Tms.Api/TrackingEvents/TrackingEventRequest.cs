using System.Text.Json.Serialization;

namespace Norvia.Tms.Api.TrackingEvents;

/// <summary>
/// A Tracking event in the Canonical Model (contracts/canonical-model/tracking-event.v1.schema.json), as the Carrier-tracking
/// Logic App sends it after mapping the Carrier's own format. The correlation ID, which API Management creates for the
/// event, is in <c>X-Correlation-ID</c>.
/// </summary>
/// <param name="EventId">The reporter's own ID for the event, unique per source: sent again, it is recognised as a repeat.</param>
/// <param name="Source">Who reported it. The Carrier comes from API Management's token check (ADR 0005), never from the Carrier's own payload.</param>
/// <param name="CarrierBookingReference">The Carrier's own reference for its booking: with the Carrier, it finds the Shipment.</param>
/// <param name="CarrierBookingNumber">The TMS's own Carrier booking number, used only when the reference isn't given or doesn't match.</param>
/// <param name="TripNumber">An Own fleet event's Trip, in place of the Carrier booking reference (P12).</param>
/// <param name="ShipmentNumber">An Own fleet event's Shipment on that Trip.</param>
/// <param name="TripVersion">The version of the Trip the Driver app held, kept for tracing only.</param>
/// <param name="Type">The Tracking event type's name; a string, so an unknown one is refused with a code of its own.</param>
/// <param name="EventTime">When it happened, with the offset of where it happened.</param>
/// <remarks>
/// What the Canonical Model requires is nullable here and checked by the endpoint, so a missing field is refused with a
/// stable code instead of the framework's bare 400.
/// </remarks>
public sealed record TrackingEventRequest(
    string? EventId = null,
    TrackingSourceRequest? Source = null,
    string? CarrierBookingReference = null,
    string? CarrierBookingNumber = null,
    string? TripNumber = null,
    string? ShipmentNumber = null,
    int? TripVersion = null,
    string? Type = null,
    DateTimeOffset? EventTime = null,
    TrackingLocationRequest? Location = null,
    DateTimeOffset? Eta = null,
    string? Remark = null);

/// <summary>Who reported a Tracking event: a Carrier, with its ID, or Own fleet, with the Driver's ID.</summary>
/// <param name="DriverId">For Own fleet, the Driver ID from the Driver's token, which the Driver reports Logic App passes on (ADR 0005), never the Driver app's own.</param>
public sealed record TrackingSourceRequest([property: JsonRequired] TrackingSourceType Type, string? CarrierId = null, string? DriverId = null);

public enum TrackingSourceType
{
    Carrier,
    OwnFleet,
}

/// <summary>Where a Tracking event happened.</summary>
/// <param name="CountryCode">ISO 3166-1 alpha-2.</param>
public sealed record TrackingLocationRequest(string? Place = null, string? Postcode = null, string? CountryCode = null);
