using System.Net;
using System.Text.Json.Nodes;
using Norvia.Tms.TestSupport.ShipperTracking;
using Norvia.Tms.TestSupport.TrackingEvents;

namespace Norvia.Tms.TestSupport.ConsigneeTracking;

/// <summary>What the Consignee tracking tests do at the TMS's boundary: ask as API Management does for a tracking code, and read or replace a code as the Planner does (ADR 0052).</summary>
internal static class ConsigneeTrackingCalls
{
    /// <summary>A well-formed code that no Transport Order holds.</summary>
    public const string UnknownCode = "ZZZZZZZZZZZZZZZZ";

    /// <summary>Asks for the view a code opens as API Management does, with the code in its header and a correlation ID, and returns the raw answer.</summary>
    public static async Task<HttpResponseMessage> ViewAsync(
        this TmsFixture tms, HttpClient caller, string? trackingCode, CancellationToken cancellationToken, string? correlationId = null)
    {
        ArgumentNullException.ThrowIfNull(tms);
        ArgumentNullException.ThrowIfNull(caller);
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri("/v1/consignee-tracking/view", UriKind.Relative));
        if (trackingCode is not null)
        {
            request.Headers.Add("X-Tracking-Code", trackingCode);
        }

        request.Headers.Add("X-Correlation-ID", correlationId ?? TmsCalls.NewCorrelationId());
        return await caller.SendAsync(request, cancellationToken);
    }

    /// <summary>The raw body of the view a code opens, which the TMS must answer.</summary>
    public static async Task<string> ViewBodyAsync(this TmsFixture tms, string trackingCode, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(tms);
        using var response = await tms.ViewAsync(tms.ConsigneeTracking, trackingCode, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK, body);
        return body;
    }

    /// <summary>The view a code opens, which the TMS must answer.</summary>
    public static async Task<JsonNode> ViewedAsync(this TmsFixture tms, string trackingCode, CancellationToken cancellationToken) =>
        JsonNode.Parse(await tms.ViewBodyAsync(trackingCode, cancellationToken))!;

    /// <summary>The Transport Order's tracking code as the Planner reads it, through the TMS UI.</summary>
    public static async Task<string> TrackingCodeOfAsync(this TmsFixture tms, string transportOrderNumber, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(tms);
        using var response = await tms.TmsUi.GetAsync(new Uri($"/v1/transport-orders/{transportOrderNumber}/tracking-code", UriKind.Relative), cancellationToken);
        return await CodeOfAsync(response, cancellationToken);
    }

    /// <summary>Replaces the Transport Order's tracking code as the Planner does, through the TMS UI, and returns the new one.</summary>
    public static async Task<string> ReplacedTrackingCodeOfAsync(this TmsFixture tms, string transportOrderNumber, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(tms);
        using var response = await tms.TmsUi.PostAsync(new Uri($"/v1/transport-orders/{transportOrderNumber}/tracking-code/replace", UriKind.Relative), content: null, cancellationToken);
        return await CodeOfAsync(response, cancellationToken);
    }

    /// <summary>The code of a Shipment's Transport Order, as the Planner reads it.</summary>
    public static async Task<string> CodeOfShipmentAsync(this TmsFixture tms, string shipmentNumber, CancellationToken cancellationToken) =>
        await tms.TrackingCodeOfAsync(await tms.TransportOrderOfAsync(shipmentNumber, cancellationToken), cancellationToken);

    private static async Task<string> CodeOfAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK, body);
        return JsonNode.Parse(body)!["trackingCode"]!.GetValue<string>();
    }

    /// <summary>A Transport Order the TMS accepted, with the code its Consignee's link carries.</summary>
    public static async Task<(string Number, string Code)> AcceptedWithCodeAsync(this TmsFixture tms, CancellationToken cancellationToken)
    {
        var number = await tms.SubmitAcceptedAsync(Bookings.Valid(TmsCalls.NewReference()), TmsCalls.NewCorrelationId(), cancellationToken);
        return (number, await tms.TrackingCodeOfAsync(number, cancellationToken));
    }

    /// <summary>A Booked Shipment's Transport Order, with the code its Consignee's link carries.</summary>
    public static async Task<(string ShipmentNumber, string Reference, string Number, string Code)> BookedWithCodeAsync(
        this TmsFixture tms, CancellationToken cancellationToken)
    {
        var (shipmentNumber, reference) = await tms.BookedShipmentAsync(cancellationToken);
        var number = await tms.TransportOrderOfAsync(shipmentNumber, cancellationToken);
        return (shipmentNumber, reference, number, await tms.TrackingCodeOfAsync(number, cancellationToken));
    }
}
