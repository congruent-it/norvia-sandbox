using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;

namespace Norvia.Tms.Api.IntegrationTests.CarrierBookings;

/// <summary>What the Carrier booking answer tests do at the TMS's boundary: confirm or reject as the Carrier-answer Logic App does.</summary>
internal static class CarrierBookingCalls
{
    /// <summary>Posts an answer body of the caller's own shape, for a call the request-level validation must refuse.</summary>
    public static async Task<HttpResponseMessage> AnswerAsync(
        this TmsFixture tms, string carrierBookingNumber, JsonObject answer, CancellationToken cancellationToken, string? correlationId = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri($"/v1/carrier-bookings/{carrierBookingNumber}/answer", UriKind.Relative))
        {
            Content = JsonContent.Create(answer),
        };
        request.Headers.Add("X-Correlation-ID", correlationId ?? Guid.NewGuid().ToString());
        return await tms.CarrierAnswer.SendAsync(request, cancellationToken);
    }

    /// <summary>Confirms the Carrier booking as the Carrier-answer Logic App does, under the given correlation ID or a new one.</summary>
    public static async Task<HttpResponseMessage> ConfirmAsync(
        this TmsFixture tms,
        string carrierBookingNumber,
        string carrierId,
        string carrierBookingReference,
        CancellationToken cancellationToken,
        DateTimeOffset? plannedCollectionTime = null,
        string? correlationId = null)
    {
        var answer = new JsonObject
        {
            ["carrierId"] = carrierId,
            ["outcome"] = "Confirmed",
            ["carrierBookingReference"] = carrierBookingReference,
            ["plannedCollectionTime"] = plannedCollectionTime is null ? null : JsonValue.Create(plannedCollectionTime.Value),
        };
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri($"/v1/carrier-bookings/{carrierBookingNumber}/answer", UriKind.Relative))
        {
            Content = JsonContent.Create(answer),
        };
        request.Headers.Add("X-Correlation-ID", correlationId ?? Guid.NewGuid().ToString());
        return await tms.CarrierAnswer.SendAsync(request, cancellationToken);
    }

    /// <summary>Confirms the Carrier booking, which the TMS must do, and returns what it answered.</summary>
    public static async Task<JsonNode> ConfirmedAsync(
        this TmsFixture tms,
        string carrierBookingNumber,
        string carrierId,
        string carrierBookingReference,
        CancellationToken cancellationToken,
        DateTimeOffset? plannedCollectionTime = null,
        string? correlationId = null)
    {
        using var response =
            await tms.ConfirmAsync(carrierBookingNumber, carrierId, carrierBookingReference, cancellationToken, plannedCollectionTime, correlationId);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK, body);
        return JsonNode.Parse(body)!;
    }

    /// <summary>Confirms the Carrier booking, which the TMS must refuse with the status, and returns its problem.</summary>
    public static async Task<JsonNode> ConfirmRefusedAsync(
        this TmsFixture tms,
        string carrierBookingNumber,
        string carrierId,
        string carrierBookingReference,
        HttpStatusCode status,
        CancellationToken cancellationToken,
        DateTimeOffset? plannedCollectionTime = null,
        string? correlationId = null)
    {
        using var response =
            await tms.ConfirmAsync(carrierBookingNumber, carrierId, carrierBookingReference, cancellationToken, plannedCollectionTime, correlationId);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        response.StatusCode.Should().Be(status, body);
        return JsonNode.Parse(body)!;
    }

    /// <summary>Rejects the Carrier booking as the Carrier-answer Logic App does, under the given correlation ID or a new one.</summary>
    public static async Task<HttpResponseMessage> RejectAsync(
        this TmsFixture tms,
        string carrierBookingNumber,
        string carrierId,
        string reasonCode,
        CancellationToken cancellationToken,
        string? reasonText = null,
        string? correlationId = null)
    {
        var answer = new JsonObject
        {
            ["carrierId"] = carrierId,
            ["outcome"] = "Rejected",
            ["reasonCode"] = reasonCode,
            ["reasonText"] = reasonText,
        };
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri($"/v1/carrier-bookings/{carrierBookingNumber}/answer", UriKind.Relative))
        {
            Content = JsonContent.Create(answer),
        };
        request.Headers.Add("X-Correlation-ID", correlationId ?? Guid.NewGuid().ToString());
        return await tms.CarrierAnswer.SendAsync(request, cancellationToken);
    }

    /// <summary>Rejects the Carrier booking, which the TMS must do, and returns what it answered.</summary>
    public static async Task<JsonNode> RejectedAsync(
        this TmsFixture tms,
        string carrierBookingNumber,
        string carrierId,
        string reasonCode,
        CancellationToken cancellationToken,
        string? reasonText = null,
        string? correlationId = null)
    {
        using var response = await tms.RejectAsync(carrierBookingNumber, carrierId, reasonCode, cancellationToken, reasonText, correlationId);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK, body);
        return JsonNode.Parse(body)!;
    }

    /// <summary>Rejects the Carrier booking, which the TMS must refuse with the status, and returns its problem.</summary>
    public static async Task<JsonNode> RejectRefusedAsync(
        this TmsFixture tms,
        string carrierBookingNumber,
        string carrierId,
        string reasonCode,
        HttpStatusCode status,
        CancellationToken cancellationToken,
        string? reasonText = null,
        string? correlationId = null)
    {
        using var response = await tms.RejectAsync(carrierBookingNumber, carrierId, reasonCode, cancellationToken, reasonText, correlationId);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        response.StatusCode.Should().Be(status, body);
        return JsonNode.Parse(body)!;
    }
}
