using System.Text.Json.Serialization;
using Norvia.Tms.Domain.TrackingEvents;

namespace Norvia.Tms.Api.TrackingEvents;

/// <summary>
/// A Tracking event in the Canonical Model (contracts/canonical-model/tracking-event.v1.schema.json), as the Carrier-tracking
/// Logic App sends it after mapping the Carrier's own format. The correlation ID, which API Management creates for the
/// event, is in <c>X-Correlation-ID</c>.
/// </summary>
/// <param name="EventId">The reporter's own ID for the event, unique per source: sent again, it is recognised as a repeat.</param>
/// <param name="Source">Who reported it. The Carrier comes from API Management's token check (ADR 0005), never from the Carrier's own payload.</param>
/// <param name="CarrierBookingReference">The Carrier's own reference for its booking: with the Carrier, it finds the Shipment.</param>
/// <param name="EventTime">When it happened, with the offset of where it happened.</param>
public sealed record TrackingEventRequest(
    [property: JsonRequired] string EventId,
    [property: JsonRequired] TrackingSourceRequest Source,
    [property: JsonRequired] string CarrierBookingReference,
    [property: JsonRequired] TrackingEventType Type,
    [property: JsonRequired] DateTimeOffset EventTime,
    [property: JsonRequired] TrackingLocationRequest Location,
    DateTimeOffset? Eta = null,
    string? Remark = null);

/// <summary>Who reported a Tracking event: a Carrier, with its ID, or Own fleet, with none.</summary>
public sealed record TrackingSourceRequest([property: JsonRequired] TrackingSourceType Type, string? CarrierId = null);

public enum TrackingSourceType
{
    Carrier,
    OwnFleet,
}

/// <summary>Where a Tracking event happened.</summary>
/// <param name="CountryCode">ISO 3166-1 alpha-2.</param>
public sealed record TrackingLocationRequest(
    [property: JsonRequired] string Place,
    [property: JsonRequired] string Postcode,
    [property: JsonRequired] string CountryCode);
