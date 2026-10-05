using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Norvia.Tms.Api.IntegrationTests.CarrierBookings;
using Norvia.Tms.Api.IntegrationTests.Planning;

namespace Norvia.Tms.Api.IntegrationTests.TrackingEvents;

/// <summary>What the Tracking event tests do at the TMS's boundary: record an event as the Carrier-tracking Logic App does.</summary>
internal static class TrackingEventCalls
{
    public static JsonObject CarrierSource(string carrierId = PlanningCalls.CarrierId) => new() { ["type"] = "Carrier", ["carrierId"] = carrierId };

    /// <summary>A Canonical Model Collected event for the Carrier's booking reference, at a location in Rotterdam.</summary>
    public static JsonObject Collected(
        string carrierBookingReference,
        DateTimeOffset eventTime,
        string? eventId = null,
        JsonObject? source = null,
        DateTimeOffset? eta = null) =>
        Event("Collected", carrierBookingReference, eventTime, eventId, source, eta);

    /// <summary>A Canonical Model Tracking event of the type for the Carrier's booking reference, at a location in Rotterdam.</summary>
    public static JsonObject Event(
        string type,
        string carrierBookingReference,
        DateTimeOffset eventTime,
        string? eventId = null,
        JsonObject? source = null,
        DateTimeOffset? eta = null)
    {
        var trackingEvent = new JsonObject
        {
            ["eventId"] = eventId ?? Guid.NewGuid().ToString(),
            ["source"] = source ?? CarrierSource(),
            ["carrierBookingReference"] = carrierBookingReference,
            ["type"] = type,
            ["eventTime"] = JsonValue.Create(eventTime),
            ["location"] = new JsonObject { ["place"] = "Rotterdam", ["postcode"] = "3011 AA", ["countryCode"] = "NL" },
            ["remark"] = $"{type} at dock 4",
        };
        if (eta is not null)
        {
            trackingEvent["eta"] = JsonValue.Create(eta.Value);
        }

        return trackingEvent;
    }

    /// <summary>Records the event as the Carrier-tracking Logic App does, under the given correlation ID or a new one.</summary>
    public static async Task<HttpResponseMessage> RecordAsync(
        this TmsFixture tms, JsonObject trackingEvent, CancellationToken cancellationToken, string? correlationId = null) =>
        await tms.RecordAsync(tms.TrackingRecord, trackingEvent, cancellationToken, correlationId);

    public static async Task<HttpResponseMessage> RecordAsync(
        this TmsFixture tms, HttpClient caller, JsonObject trackingEvent, CancellationToken cancellationToken, string? correlationId = null)
    {
        ArgumentNullException.ThrowIfNull(tms);
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri("/v1/tracking-events", UriKind.Relative))
        {
            Content = JsonContent.Create(trackingEvent),
        };
        request.Headers.Add("X-Correlation-ID", correlationId ?? Guid.NewGuid().ToString());
        return await caller.SendAsync(request, cancellationToken);
    }

    /// <summary>Records the event, which the TMS must accept, and returns what it answered.</summary>
    public static async Task<JsonNode> RecordedAsync(
        this TmsFixture tms, JsonObject trackingEvent, CancellationToken cancellationToken, string? correlationId = null)
    {
        using var response = await tms.RecordAsync(trackingEvent, cancellationToken, correlationId);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK, body);
        return JsonNode.Parse(body)!;
    }

    /// <summary>Records the event, which the TMS must refuse with the status, and returns its problem.</summary>
    public static async Task<JsonNode> RecordRefusedAsync(
        this TmsFixture tms, JsonObject trackingEvent, HttpStatusCode status, CancellationToken cancellationToken, string? correlationId = null)
    {
        using var response = await tms.RecordAsync(trackingEvent, cancellationToken, correlationId);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        response.StatusCode.Should().Be(status, body);
        return JsonNode.Parse(body)!;
    }

    /// <summary>A Carrier Shipment the Carrier confirmed, so it is Booked, with the Carrier's own booking reference.</summary>
    public static async Task<(string ShipmentNumber, string CarrierBookingReference)> BookedShipmentAsync(
        this TmsFixture tms, CancellationToken cancellationToken)
    {
        var (carrierBookingNumber, shipmentNumber) = await tms.ReleasedCarrierBookingAsync(cancellationToken);
        var reference = $"VDT-{Guid.NewGuid():N}"[..20];
        var confirmed = await tms.ConfirmedAsync(carrierBookingNumber, PlanningCalls.CarrierId, reference, cancellationToken);
        confirmed["shipmentStatus"]!.GetValue<string>().Should().Be("Booked");
        return (shipmentNumber, reference);
    }
}
