using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Azure.Messaging.ServiceBus;
using Norvia.Tms.Api.IntegrationTests.TransportOrders;

namespace Norvia.Tms.Api.IntegrationTests.Planning;

/// <summary>What the planning tests do at the TMS's boundary: assign as the TMS UI does, and read the query API.</summary>
internal static class PlanningCalls
{
    /// <summary>The test seed's own active Carrier.</summary>
    public const string CarrierId = "CAR-TEST-0001";

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

    /// <summary>Every ShipmentPlanned published so far for the Transport Order, whatever its correlation ID.</summary>
    public static IReadOnlyList<ServiceBusReceivedMessage> ShipmentsPlannedFor(this TmsFixture tms, string transportOrderNumber) =>
        [.. tms.Published.Where(m =>
            m.Subject == "ShipmentPlanned"
            && m.ApplicationProperties.TryGetValue("transportOrderNumber", out var number)
            && Equals(number, transportOrderNumber))];
}
