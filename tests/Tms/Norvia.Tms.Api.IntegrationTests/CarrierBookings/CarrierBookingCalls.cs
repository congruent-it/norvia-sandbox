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

    /// <summary>Reports the Carrier booking as sent, as the P4 booking Logic App does, under the given correlation ID or a new one.</summary>
    public static async Task<HttpResponseMessage> ReportSentAsync(
        this TmsFixture tms, string carrierBookingNumber, CancellationToken cancellationToken, string? correlationId = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri($"/v1/carrier-bookings/{carrierBookingNumber}/report-sent", UriKind.Relative));
        request.Headers.Add("X-Correlation-ID", correlationId ?? Guid.NewGuid().ToString());
        return await tms.CarrierReport.SendAsync(request, cancellationToken);
    }

    /// <summary>Reports the Carrier booking as sent, which the TMS must accept, and returns what it answered.</summary>
    public static async Task<JsonNode> ReportedSentAsync(this TmsFixture tms, string carrierBookingNumber, CancellationToken cancellationToken, string? correlationId = null)
    {
        using var response = await tms.ReportSentAsync(carrierBookingNumber, cancellationToken, correlationId);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK, body);
        return JsonNode.Parse(body)!;
    }

    /// <summary>
    /// Reports the Carrier booking as not delivered with a technical cause, as the P4 booking Logic App does, under the
    /// given correlation ID or a new one.
    /// </summary>
    public static async Task<HttpResponseMessage> ReportNotDeliveredAsync(
        this TmsFixture tms,
        string carrierBookingNumber,
        CancellationToken cancellationToken,
        int statusCode = 503,
        string message = "Service unavailable.",
        string? correlationId = null)
    {
        var report = new JsonObject { ["statusCode"] = statusCode, ["message"] = message };
        using var request =
            new HttpRequestMessage(HttpMethod.Post, new Uri($"/v1/carrier-bookings/{carrierBookingNumber}/report-not-delivered", UriKind.Relative))
            {
                Content = JsonContent.Create(report),
            };
        request.Headers.Add("X-Correlation-ID", correlationId ?? Guid.NewGuid().ToString());
        return await tms.CarrierReport.SendAsync(request, cancellationToken);
    }

    /// <summary>Reports the Carrier booking as not delivered, which the TMS must accept, and returns what it answered.</summary>
    public static async Task<JsonNode> ReportedNotDeliveredAsync(
        this TmsFixture tms,
        string carrierBookingNumber,
        CancellationToken cancellationToken,
        int statusCode = 503,
        string message = "Service unavailable.",
        string? correlationId = null)
    {
        using var response = await tms.ReportNotDeliveredAsync(carrierBookingNumber, cancellationToken, statusCode, message, correlationId);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK, body);
        return JsonNode.Parse(body)!;
    }

    /// <summary>Withdraws the Carrier booking as the Planner does, through the TMS UI, under the given idempotency key or a new one.</summary>
    public static async Task<HttpResponseMessage> WithdrawAsync(
        this TmsFixture tms, string carrierBookingNumber, CancellationToken cancellationToken, string? idempotencyKey = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri($"/v1/carrier-bookings/{carrierBookingNumber}/withdraw", UriKind.Relative));
        request.Headers.Add("Idempotency-Key", idempotencyKey ?? Guid.NewGuid().ToString());
        return await tms.TmsUi.SendAsync(request, cancellationToken);
    }

    /// <summary>Withdraws the Carrier booking, which the TMS must do, and returns what it answered.</summary>
    public static async Task<JsonNode> WithdrawnAsync(this TmsFixture tms, string carrierBookingNumber, CancellationToken cancellationToken, string? idempotencyKey = null)
    {
        using var response = await tms.WithdrawAsync(carrierBookingNumber, cancellationToken, idempotencyKey);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK, body);
        return JsonNode.Parse(body)!;
    }

    /// <summary>Withdraws the Carrier booking, which the TMS must refuse with the status, and returns its problem.</summary>
    public static async Task<JsonNode> WithdrawRefusedAsync(
        this TmsFixture tms, string carrierBookingNumber, HttpStatusCode status, CancellationToken cancellationToken, string? idempotencyKey = null)
    {
        using var response = await tms.WithdrawAsync(carrierBookingNumber, cancellationToken, idempotencyKey);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        response.StatusCode.Should().Be(status, body);
        return JsonNode.Parse(body)!;
    }

    /// <summary>The Planner's list of Requested Carrier bookings awaiting an answer, from the query API.</summary>
    public static async Task<JsonArray> ListBookingsAwaitingAnswerAsync(this TmsFixture tms, CancellationToken cancellationToken)
    {
        using var response = await tms.Tms.GetAsync(new Uri("/v1/shipments/bookings-awaiting-answer", UriKind.Relative), cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK, body);
        return JsonNode.Parse(body)!.AsArray();
    }
}
