using System.Net.Http.Json;
using System.Text.Json.Nodes;

namespace Norvia.Tms.Api.IntegrationTests.Exceptions;

/// <summary>What the Shipper notification tests do at the TMS's boundary: the Planner notifies the Shipper, and the Logic App reports.</summary>
internal static class ShipperNotificationCalls
{
    /// <summary>The message the Notify Shipper form starts with, as the Planner's UI gets it.</summary>
    public static async Task<HttpResponseMessage> GetShipperNotificationTemplateAsync(
        this TmsFixture tms, string exceptionNumber, CancellationToken cancellationToken) =>
        await tms.TmsUi.GetAsync(new Uri($"/v1/exceptions/{exceptionNumber}/shipper-notification-template", UriKind.Relative), cancellationToken);

    /// <summary>Notifies the Shipper as the Planner does, through the TMS UI.</summary>
    public static async Task<HttpResponseMessage> NotifyShipperAsync(
        this TmsFixture tms, string exceptionNumber, CancellationToken cancellationToken, JsonObject? notification = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri($"/v1/exceptions/{exceptionNumber}/notify-shipper", UriKind.Relative))
        {
            Content = JsonContent.Create(notification ?? Notification()),
        };
        return await tms.TmsUi.SendAsync(request, cancellationToken);
    }

    public static JsonObject Notification(string? message = "Your delivery is delayed.", string? planner = "Anna de Vries") =>
        new() { ["message"] = message, ["planner"] = planner };

    /// <summary>Reports a notification as sent, as the Shipper notification Logic App does, under the given correlation ID or a new one.</summary>
    public static async Task<HttpResponseMessage> ReportShipperNotificationSentAsync(
        this TmsFixture tms, string notificationId, CancellationToken cancellationToken, string? correlationId = null, HttpClient? caller = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri($"/v1/shipper-notifications/{notificationId}/report-sent", UriKind.Relative));
        request.Headers.Add("X-Correlation-ID", correlationId ?? Guid.NewGuid().ToString());
        return await (caller ?? tms.ShipperNotificationReport).SendAsync(request, cancellationToken);
    }

    /// <summary>Reports a notification as not delivered, with a technical cause, as the Shipper notification Logic App does.</summary>
    public static async Task<HttpResponseMessage> ReportShipperNotificationNotDeliveredAsync(
        this TmsFixture tms,
        string notificationId,
        CancellationToken cancellationToken,
        int statusCode = 503,
        string message = "Service unavailable.",
        string? correlationId = null,
        HttpClient? caller = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri($"/v1/shipper-notifications/{notificationId}/report-not-delivered", UriKind.Relative))
        {
            Content = JsonContent.Create(new JsonObject { ["statusCode"] = statusCode, ["message"] = message }),
        };
        request.Headers.Add("X-Correlation-ID", correlationId ?? Guid.NewGuid().ToString());
        return await (caller ?? tms.ShipperNotificationReport).SendAsync(request, cancellationToken);
    }
}
