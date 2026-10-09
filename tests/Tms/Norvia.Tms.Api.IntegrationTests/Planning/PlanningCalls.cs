using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Azure.Messaging.ServiceBus;
using Norvia.Tms.Api.IntegrationTests.TransportOrders;

namespace Norvia.Tms.Api.IntegrationTests.Planning;

/// <summary>What the planning tests do at the TMS's boundary: assign, reassign and release as the TMS UI does, and read the query API.</summary>
internal static class PlanningCalls
{
    /// <summary>The test seed's own active Carrier.</summary>
    public const string CarrierId = "CAR-TEST-0001";

    /// <summary>The test seed's inactive Carrier.</summary>
    public const string InactiveCarrierId = "CAR-TEST-0002";

    /// <summary>The test seed's second active Carrier, to reassign to.</summary>
    public const string OtherCarrierId = "CAR-TEST-0003";

    public static JsonObject ToCarrier(string carrierId = CarrierId) => new() { ["assignedTo"] = "Carrier", ["carrierId"] = carrierId };

    public static JsonObject ToOwnFleet() => new() { ["assignedTo"] = "OwnFleet" };

    /// <summary>
    /// Assigns the Transport Order as the Planner does, through the TMS UI, under the given idempotency key or a new one.
    /// </summary>
    public static async Task<HttpResponseMessage> AssignAsync(
        this TmsFixture tms, string number, JsonObject assignment, CancellationToken cancellationToken, string? idempotencyKey = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri($"/v1/transport-orders/{number}/assign", UriKind.Relative))
        {
            Content = JsonContent.Create(assignment),
        };
        request.Headers.Add("Idempotency-Key", idempotencyKey ?? Guid.NewGuid().ToString());
        return await tms.TmsUi.SendAsync(request, cancellationToken);
    }

    /// <summary>Assigns the Transport Order, which the TMS must plan, and returns what it answered.</summary>
    public static async Task<JsonNode> AssignPlannedAsync(this TmsFixture tms, string number, JsonObject assignment, CancellationToken cancellationToken)
    {
        using var response = await tms.AssignAsync(number, assignment, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.Created, body);
        return JsonNode.Parse(body)!;
    }

    /// <summary>Assigns the Transport Order, which the TMS must refuse with the status, and returns its problem.</summary>
    public static async Task<JsonNode> AssignRefusedAsync(
        this TmsFixture tms, string number, JsonObject assignment, HttpStatusCode status, CancellationToken cancellationToken)
    {
        using var response = await tms.AssignAsync(number, assignment, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        response.StatusCode.Should().Be(status, body);
        return JsonNode.Parse(body)!;
    }

    /// <summary>
    /// Reassigns the Shipment as the Planner does, through the TMS UI, under the given idempotency key or a new one.
    /// </summary>
    public static async Task<HttpResponseMessage> ReassignAsync(
        this TmsFixture tms, string shipmentNumber, JsonObject reassignment, CancellationToken cancellationToken, string? idempotencyKey = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri($"/v1/shipments/{shipmentNumber}/reassign", UriKind.Relative))
        {
            Content = JsonContent.Create(reassignment),
        };
        request.Headers.Add("Idempotency-Key", idempotencyKey ?? Guid.NewGuid().ToString());
        return await tms.TmsUi.SendAsync(request, cancellationToken);
    }

    /// <summary>Reassigns the Shipment, which the TMS must do, and returns what it answered.</summary>
    public static async Task<JsonNode> ReassignedAsync(this TmsFixture tms, string shipmentNumber, JsonObject reassignment, CancellationToken cancellationToken)
    {
        using var response = await tms.ReassignAsync(shipmentNumber, reassignment, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK, body);
        return JsonNode.Parse(body)!;
    }

    /// <summary>Reassigns the Shipment, which the TMS must refuse with the status, and returns its problem.</summary>
    public static async Task<JsonNode> ReassignRefusedAsync(
        this TmsFixture tms, string shipmentNumber, JsonObject reassignment, HttpStatusCode status, CancellationToken cancellationToken)
    {
        using var response = await tms.ReassignAsync(shipmentNumber, reassignment, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        response.StatusCode.Should().Be(status, body);
        return JsonNode.Parse(body)!;
    }

    /// <summary>Releases the Shipment as the Planner does, through the TMS UI, under the given idempotency key or a new one.</summary>
    public static async Task<HttpResponseMessage> ReleaseAsync(
        this TmsFixture tms, string shipmentNumber, CancellationToken cancellationToken, string? idempotencyKey = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri($"/v1/shipments/{shipmentNumber}/release", UriKind.Relative));
        request.Headers.Add("Idempotency-Key", idempotencyKey ?? Guid.NewGuid().ToString());
        return await tms.TmsUi.SendAsync(request, cancellationToken);
    }

    /// <summary>Releases the Shipment at a Spot price as the Planner does, through the TMS UI, or as the given caller does.</summary>
    public static async Task<HttpResponseMessage> ReleaseWithSpotPriceAsync(
        this TmsFixture tms, string shipmentNumber, JsonObject spotPrice, CancellationToken cancellationToken, HttpClient? caller = null, string? idempotencyKey = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri($"/v1/shipments/{shipmentNumber}/release-with-spot-price", UriKind.Relative))
        {
            Content = JsonContent.Create(spotPrice),
        };
        request.Headers.Add("Idempotency-Key", idempotencyKey ?? Guid.NewGuid().ToString());
        return await (caller ?? tms.TmsUi).SendAsync(request, cancellationToken);
    }

    /// <summary>The body of a release with a Spot price.</summary>
    public static JsonObject Spot(decimal? amount, string? remark) => new() { ["amount"] = amount, ["remark"] = remark };

    /// <summary>Releases the Shipment, which the TMS must do, and returns what it answered.</summary>
    public static async Task<JsonNode> ReleasedAsync(this TmsFixture tms, string shipmentNumber, CancellationToken cancellationToken)
    {
        using var response = await tms.ReleaseAsync(shipmentNumber, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK, body);
        return JsonNode.Parse(body)!;
    }

    /// <summary>Releases the Shipment, which the TMS must refuse with the status, and returns its problem.</summary>
    public static async Task<JsonNode> ReleaseRefusedAsync(this TmsFixture tms, string shipmentNumber, HttpStatusCode status, CancellationToken cancellationToken)
    {
        using var response = await tms.ReleaseAsync(shipmentNumber, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        response.StatusCode.Should().Be(status, body);
        return JsonNode.Parse(body)!;
    }

    /// <summary>The Planner's list of Shipments to release, from the query API.</summary>
    public static async Task<JsonArray> ListShipmentsToReleaseAsync(this TmsFixture tms, CancellationToken cancellationToken)
    {
        using var response = await tms.Tms.GetAsync(new Uri("/v1/shipments/to-release", UriKind.Relative), cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK, body);
        return JsonNode.Parse(body)!.AsArray();
    }

    /// <summary>Submits a booking the TMS accepts and waits until it is rated, so later events on its trail are the planning's.</summary>
    public static async Task<string> SubmitAcceptedAsync(this TmsFixture tms, CancellationToken cancellationToken)
    {
        var intake = TmsCalls.NewCorrelationId();
        var number = await tms.SubmitAcceptedAsync(Bookings.Valid(TmsCalls.NewReference()), intake, cancellationToken);
        await tms.WaitForRatingResultAsync(number, "Rated", cancellationToken);
        return number;
    }

    public static async Task<JsonNode> GetShipmentAsync(this TmsFixture tms, string number, CancellationToken cancellationToken)
    {
        using var response = await tms.Tms.GetAsync(new Uri($"/v1/shipments/{number}", UriKind.Relative), cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK, body);
        return JsonNode.Parse(body)!;
    }

    /// <summary>Every ShipmentReassigned published so far for the Shipment, whatever its correlation ID.</summary>
    public static IReadOnlyList<ServiceBusReceivedMessage> ShipmentsReassigned(this TmsFixture tms, string shipmentNumber) =>
        [.. tms.Published.Where(m =>
            m.Subject == "ShipmentReassigned"
            && m.ApplicationProperties.TryGetValue("shipmentNumber", out var number)
            && Equals(number, shipmentNumber))];

    /// <summary>Every ShipmentReleased published so far for the Shipment, whatever its correlation ID.</summary>
    public static IReadOnlyList<ServiceBusReceivedMessage> ShipmentsReleased(this TmsFixture tms, string shipmentNumber) =>
        [.. tms.Published.Where(m =>
            m.Subject == "ShipmentReleased"
            && m.ApplicationProperties.TryGetValue("shipmentNumber", out var number)
            && Equals(number, shipmentNumber))];

    /// <summary>Every ShipmentPlanned published so far for the Transport Order, whatever its correlation ID.</summary>
    public static IReadOnlyList<ServiceBusReceivedMessage> ShipmentsPlannedFor(this TmsFixture tms, string transportOrderNumber) =>
        [.. tms.Published.Where(m =>
            m.Subject == "ShipmentPlanned"
            && m.ApplicationProperties.TryGetValue("transportOrderNumber", out var number)
            && Equals(number, transportOrderNumber))];
}
