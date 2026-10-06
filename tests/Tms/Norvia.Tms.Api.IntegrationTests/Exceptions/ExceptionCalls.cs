using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Azure.Messaging.ServiceBus;
using Norvia.Tms.Api.IntegrationTests.TrackingEvents;
using Norvia.Tms.Api.IntegrationTests.TransportOrders;

namespace Norvia.Tms.Api.IntegrationTests.Exceptions;

/// <summary>What the Exception tests do at the TMS's boundary: a Carrier reports a failed delivery, and the Planner works the list.</summary>
internal static class ExceptionCalls
{
    /// <summary>The Open Exceptions, oldest first, from the query API.</summary>
    public static async Task<JsonArray> ListOpenExceptionsAsync(this TmsFixture tms, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(tms);
        using var response = await tms.Tms.GetAsync(new Uri("/v1/exceptions/open", UriKind.Relative), cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK, body);
        return JsonNode.Parse(body)!.AsArray();
    }

    /// <summary>The Open Exceptions of the Shipment, from the query API.</summary>
    public static async Task<IReadOnlyList<JsonNode>> OpenExceptionsOfAsync(this TmsFixture tms, string shipmentNumber, CancellationToken cancellationToken) =>
        [.. (await tms.ListOpenExceptionsAsync(cancellationToken)).Where(open => open!["shipmentNumber"]!.GetValue<string>() == shipmentNumber).Select(open => open!)];

    /// <summary>A Booked Carrier Shipment for which the Carrier reported a failed delivery (<c>NDL</c>), and the correlation ID of that report.</summary>
    public static async Task<(string ShipmentNumber, string Reference, string CorrelationId)> DeliveryFailedShipmentAsync(
        this TmsFixture tms, CancellationToken cancellationToken)
    {
        var (shipmentNumber, reference) = await tms.BookedShipmentAsync(cancellationToken);
        var correlationId = TmsCalls.NewCorrelationId();
        await tms.ReportDeliveryFailedAsync(reference, cancellationToken, correlationId);
        return (shipmentNumber, reference, correlationId);
    }

    /// <summary>Records a Delivery failed Tracking event as the Carrier-tracking Logic App does, which the TMS must accept.</summary>
    public static async Task ReportDeliveryFailedAsync(
        this TmsFixture tms, string reference, CancellationToken cancellationToken, string? correlationId = null, string? eventId = null) =>
        await tms.RecordedAsync(
            TrackingEventCalls.Event("DeliveryFailed", reference, DateTimeOffset.UtcNow.AddMinutes(-1), eventId),
            cancellationToken,
            correlationId);

    /// <summary>Resolves the Exception as the Planner does, through the TMS UI.</summary>
    public static async Task<HttpResponseMessage> ResolveAsync(
        this TmsFixture tms, string exceptionNumber, JsonObject resolution, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(tms);
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri($"/v1/exceptions/{exceptionNumber}/resolve", UriKind.Relative))
        {
            Content = JsonContent.Create(resolution),
        };
        return await tms.TmsUi.SendAsync(request, cancellationToken);
    }

    /// <summary>Adds a note as the Planner does, through the TMS UI.</summary>
    public static async Task<HttpResponseMessage> AddNoteAsync(
        this TmsFixture tms, string exceptionNumber, JsonObject note, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(tms);
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri($"/v1/exceptions/{exceptionNumber}/notes", UriKind.Relative))
        {
            Content = JsonContent.Create(note),
        };
        return await tms.TmsUi.SendAsync(request, cancellationToken);
    }

    /// <summary>Agrees a delivery window as the Planner does, through the TMS UI.</summary>
    public static async Task<HttpResponseMessage> AgreeDeliveryWindowAsync(
        this TmsFixture tms, string exceptionNumber, JsonObject window, CancellationToken cancellationToken, string? idempotencyKey = null)
    {
        ArgumentNullException.ThrowIfNull(tms);
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri($"/v1/exceptions/{exceptionNumber}/delivery-window", UriKind.Relative))
        {
            Content = JsonContent.Create(window),
        };
        request.Headers.Add("Idempotency-Key", idempotencyKey ?? Guid.NewGuid().ToString());
        return await tms.TmsUi.SendAsync(request, cancellationToken);
    }

    /// <summary>Cancels the Carrier booking as the Planner does, through the TMS UI, under the given idempotency key or a new one.</summary>
    public static async Task<HttpResponseMessage> CancelCarrierBookingAsync(
        this TmsFixture tms, string exceptionNumber, JsonObject cancellation, CancellationToken cancellationToken, string? idempotencyKey = null)
    {
        ArgumentNullException.ThrowIfNull(tms);
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri($"/v1/exceptions/{exceptionNumber}/cancel-carrier-booking", UriKind.Relative))
        {
            Content = JsonContent.Create(cancellation),
        };
        request.Headers.Add("Idempotency-Key", idempotencyKey ?? Guid.NewGuid().ToString());
        return await tms.TmsUi.SendAsync(request, cancellationToken);
    }

    public static JsonObject Cancellation(string? reason = "Carrier won't make its collection")
    {
        var cancellation = new JsonObject();
        if (reason is not null)
        {
            cancellation["reason"] = reason;
        }

        return cancellation;
    }

    /// <summary>A Booked Carrier Shipment whose Carrier reported a failed delivery, with the Exception that opened and the booking's numbers.</summary>
    public static async Task<(string ShipmentNumber, string CarrierBookingNumber, string Reference, string ExceptionNumber)> BookedWithDeliveryFailedAsync(
        this TmsFixture tms, CancellationToken cancellationToken)
    {
        var (shipmentNumber, carrierBookingNumber, reference) = await tms.BookedShipmentWithNumberAsync(cancellationToken);
        await tms.ReportDeliveryFailedAsync(reference, cancellationToken);
        var exceptionNumber = (await tms.OpenExceptionsOfAsync(shipmentNumber, cancellationToken)).Single()["exceptionNumber"]!.GetValue<string>();
        return (shipmentNumber, carrierBookingNumber, reference, exceptionNumber);
    }

    public static JsonObject Window(DateTimeOffset earliest, DateTimeOffset latest) =>
        new() { ["earliest"] = earliest, ["latest"] = latest };

    /// <summary>A Collected Carrier Shipment for which the Carrier then reported a failed delivery, and the Exception that opened.</summary>
    public static async Task<(string ShipmentNumber, string ExceptionNumber)> CollectedWithDeliveryFailedAsync(this TmsFixture tms, CancellationToken cancellationToken)
    {
        var (shipmentNumber, reference) = await tms.BookedShipmentAsync(cancellationToken);
        await tms.RecordedAsync(TrackingEventCalls.Event("Collected", reference, DateTimeOffset.UtcNow.AddMinutes(-2)), cancellationToken);
        await tms.ReportDeliveryFailedAsync(reference, cancellationToken);
        var exceptionNumber = (await tms.OpenExceptionsOfAsync(shipmentNumber, cancellationToken)).Single()["exceptionNumber"]!.GetValue<string>();
        return (shipmentNumber, exceptionNumber);
    }

    public static JsonObject Note(string? text = "Phoned Kessler, redelivery Thursday", string? planner = "Anna de Vries")
    {
        var note = new JsonObject();
        if (text is not null)
        {
            note["text"] = text;
        }

        if (planner is not null)
        {
            note["planner"] = planner;
        }

        return note;
    }

    public static JsonObject Resolution(string? outcome = "Redelivered", string? remark = "Phoned the Consignee, redelivery Thursday")
    {
        var resolution = new JsonObject();
        if (outcome is not null)
        {
            resolution["outcome"] = outcome;
        }

        if (remark is not null)
        {
            resolution["remark"] = remark;
        }

        return resolution;
    }

    /// <summary>Resolves the Exception, which the TMS must do, and returns what it answered.</summary>
    public static async Task<JsonNode> ResolvedAsync(this TmsFixture tms, string exceptionNumber, JsonObject resolution, CancellationToken cancellationToken)
    {
        using var response = await tms.ResolveAsync(exceptionNumber, resolution, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK, body);
        return JsonNode.Parse(body)!;
    }

    /// <summary>Resolves the Exception, which the TMS must refuse with the status, and returns its problem.</summary>
    public static async Task<JsonNode> ResolveRefusedAsync(
        this TmsFixture tms, string exceptionNumber, JsonObject resolution, HttpStatusCode status, CancellationToken cancellationToken)
    {
        using var response = await tms.ResolveAsync(exceptionNumber, resolution, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        response.StatusCode.Should().Be(status, body);
        return JsonNode.Parse(body)!;
    }

    public static JsonNode BodyOf(ServiceBusReceivedMessage message) => JsonNode.Parse(message.Body.ToString())!;
}
