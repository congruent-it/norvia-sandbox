using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Norvia.Tms.Api.IntegrationTests.Planning;
using Norvia.Tms.Api.IntegrationTests.TransportOrders;

namespace Norvia.Tms.Api.IntegrationTests.ShipperTracking;

/// <summary>What the Shipper tracking tests do at the TMS's boundary: ask as API Management does for a Shipper, and book for one.</summary>
internal static class ShipperTrackingCalls
{
    public const string Shipper = "SHP-0001";
    public const string AnotherShipper = "SHP-0003";

    /// <summary>Asks for the Shipper's Transport Orders as API Management does, with the Shipper ID and a correlation ID, and returns the raw answer.</summary>
    public static async Task<HttpResponseMessage> ListAsync(
        this TmsFixture tms, HttpClient caller, string? shipperId, CancellationToken cancellationToken, string? correlationId = null, string? query = null)
    {
        ArgumentNullException.ThrowIfNull(tms);
        ArgumentNullException.ThrowIfNull(caller);
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri("/v1/shipper-tracking/transport-orders" + (query is null ? "" : "?" + query), UriKind.Relative));
        if (shipperId is not null)
        {
            request.Headers.Add("X-Shipper-ID", shipperId);
        }

        request.Headers.Add("X-Correlation-ID", correlationId ?? TmsCalls.NewCorrelationId());
        return await caller.SendAsync(request, cancellationToken);
    }

    /// <summary>Gets one Transport Order's tracking as API Management does, by its number, and returns the raw answer.</summary>
    public static Task<HttpResponseMessage> GetAsync(
        this TmsFixture tms, HttpClient caller, string? shipperId, string transportOrderNumber, CancellationToken cancellationToken) =>
        SendAsync(caller, shipperId, $"/v1/shipper-tracking/transport-orders/{Uri.EscapeDataString(transportOrderNumber)}", cancellationToken);

    /// <summary>Finds one Transport Order's tracking as API Management does, by the Shipper's reference, and returns the raw answer.</summary>
    public static Task<HttpResponseMessage> FindAsync(
        this TmsFixture tms, HttpClient caller, string? shipperId, string shipperReference, CancellationToken cancellationToken) =>
        SendAsync(caller, shipperId, $"/v1/shipper-tracking/transport-orders/find?shipperReference={Uri.EscapeDataString(shipperReference)}", cancellationToken);

    /// <summary>The Shipper's Transport Order by number, which the TMS must answer.</summary>
    public static async Task<JsonNode> GotAsync(this TmsFixture tms, string shipperId, string transportOrderNumber, CancellationToken cancellationToken) =>
        JsonNode.Parse(await tms.GotBodyAsync(shipperId, transportOrderNumber, cancellationToken))!;

    /// <summary>The raw body of the Shipper's Transport Order by number, for what no part of it may contain.</summary>
    public static async Task<string> GotBodyAsync(this TmsFixture tms, string shipperId, string transportOrderNumber, CancellationToken cancellationToken)
    {
        using var response = await tms.GetAsync(tms.ShipperTracking, shipperId, transportOrderNumber, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK, body);
        return body;
    }

    private static async Task<HttpResponseMessage> SendAsync(HttpClient caller, string? shipperId, string path, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(caller);
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(path, UriKind.Relative));
        if (shipperId is not null)
        {
            request.Headers.Add("X-Shipper-ID", shipperId);
        }

        request.Headers.Add("X-Correlation-ID", TmsCalls.NewCorrelationId());
        return await caller.SendAsync(request, cancellationToken);
    }

    /// <summary>The Shipper's Transport Orders, as API Management gets them, which the TMS must answer.</summary>
    public static async Task<JsonArray> ListedForAsync(this TmsFixture tms, string shipperId, CancellationToken cancellationToken) =>
        JsonNode.Parse(await tms.ListedBodyForAsync(shipperId, cancellationToken))!.AsArray();

    /// <summary>The raw body of the Shipper's Transport Orders, for what no part of it may contain.</summary>
    public static async Task<string> ListedBodyForAsync(this TmsFixture tms, string shipperId, CancellationToken cancellationToken)
    {
        using var response = await tms.ListAsync(tms.ShipperTracking, shipperId, cancellationToken, query: "pageSize=100");
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK, body);
        return body;
    }

    /// <summary>The Shipper's Transport Order with the number, which must be in the list.</summary>
    public static async Task<JsonNode> ListedAsync(this TmsFixture tms, string shipperId, string transportOrderNumber, CancellationToken cancellationToken)
    {
        var listed = await tms.ListedForAsync(shipperId, cancellationToken);
        return listed.Single(item => item!["transportOrderNumber"]!.GetValue<string>() == transportOrderNumber)!;
    }

    public static async Task<string> TrackingStatusAsync(this TmsFixture tms, string transportOrderNumber, CancellationToken cancellationToken) =>
        (await tms.ListedAsync(Shipper, transportOrderNumber, cancellationToken))["trackingStatus"]!.GetValue<string>();

    /// <summary>The number of the Transport Order the Shipment carries out.</summary>
    public static async Task<string> TransportOrderOfAsync(this TmsFixture tms, string shipmentNumber, CancellationToken cancellationToken) =>
        (await tms.GetShipmentAsync(shipmentNumber, cancellationToken))["transportOrderNumber"]!.GetValue<string>();

    /// <summary>Submits a booking for another Shipper, which the TMS must accept, and returns its number.</summary>
    public static async Task<string> SubmitAcceptedForAsync(this TmsFixture tms, string shipperId, CancellationToken cancellationToken)
    {
        var booking = Bookings.Valid(TmsCalls.NewReference());
        booking["shipperId"] = shipperId;
        return await tms.SubmitAcceptedAsync(booking, TmsCalls.NewCorrelationId(), cancellationToken);
    }

    /// <summary>Submits a booking of the Shipper's that breaks a rule, which the TMS must reject.</summary>
    public static async Task SubmitRejectedAsync(this TmsFixture tms, string shipperId, CancellationToken cancellationToken)
    {
        var booking = Bookings.Valid(TmsCalls.NewReference());
        booking["shipperId"] = shipperId;
        booking["goodsLines"] = new JsonArray();
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri("/v1/transport-orders", UriKind.Relative))
        {
            Content = JsonContent.Create(booking),
        };
        request.Headers.Add("X-Correlation-ID", TmsCalls.NewCorrelationId());
        using var response = await tms.Tms.SendAsync(request, cancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity, await response.Content.ReadAsStringAsync(cancellationToken));
    }
}
