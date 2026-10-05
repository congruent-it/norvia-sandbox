using System.Net;
using System.Net.Http.Headers;
using System.Text.Json.Nodes;
using Norvia.Tms.Api.IntegrationTests.Planning;
using Norvia.Tms.Api.IntegrationTests.TrackingEvents;

namespace Norvia.Tms.Api.IntegrationTests.ProofsOfDelivery;

/// <summary>What the Proof of delivery tests do at the TMS's boundary: record a Proof of delivery as the Carrier Proof of delivery Logic App does, and read it back.</summary>
internal static class ProofOfDeliveryCalls
{
    /// <summary>A file's name, content type and content, as one part of the multipart call.</summary>
    public sealed record SentFile(string Name, string ContentType, byte[] Content);

    /// <summary>A scanned CMR: content of its own, so a test can tell it from any other file read back.</summary>
    public static SentFile Cmr(string name = "cmr.pdf") => new(name, "application/pdf", [.. "%PDF-1.7 signed CMR "u8, .. Guid.NewGuid().ToByteArray()]);

    /// <summary>Canonical Model metadata for the Carrier's booking reference, delivered in Düsseldorf and signed clean.</summary>
    public static JsonObject Metadata(
        string carrierBookingReference,
        string? documentId = null,
        JsonObject? source = null,
        DateTimeOffset? deliveryTime = null) =>
        new()
        {
            ["documentId"] = documentId ?? Guid.NewGuid().ToString(),
            ["source"] = source ?? TrackingEventCalls.CarrierSource(),
            ["carrierBookingReference"] = carrierBookingReference,
            ["receivedBy"] = "J. Becker",
            ["deliveryTime"] = JsonValue.Create(deliveryTime ?? new DateTimeOffset(2026, 9, 25, 14, 30, 0, TimeSpan.FromHours(2))),
            ["location"] = new JsonObject { ["place"] = "Düsseldorf", ["postcode"] = "40213", ["countryCode"] = "DE" },
            ["condition"] = "Clean",
            ["remark"] = "Signed at the dock",
        };

    /// <summary>Records the Proof of delivery as the Carrier Proof of delivery Logic App does, under the given correlation ID or a new one.</summary>
    public static async Task<HttpResponseMessage> RecordProofAsync(
        this TmsFixture tms, JsonObject metadata, IReadOnlyList<SentFile> files, CancellationToken cancellationToken, string? correlationId = null) =>
        await tms.RecordProofAsync(tms.ProofOfDeliveryRecord, metadata, files, cancellationToken, correlationId);

    public static async Task<HttpResponseMessage> RecordProofAsync(
        this TmsFixture tms, HttpClient caller, JsonObject metadata, IReadOnlyList<SentFile> files, CancellationToken cancellationToken, string? correlationId = null)
    {
        ArgumentNullException.ThrowIfNull(tms);
        using var form = new MultipartFormDataContent { { new StringContent(metadata.ToJsonString()), "metadata" } };
        foreach (var file in files)
        {
            var content = new ByteArrayContent(file.Content);
            content.Headers.ContentType = MediaTypeHeaderValue.Parse(file.ContentType);
            form.Add(content, "files", file.Name);
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri("/v1/proofs-of-delivery", UriKind.Relative)) { Content = form };
        request.Headers.Add("X-Correlation-ID", correlationId ?? Guid.NewGuid().ToString());
        return await caller.SendAsync(request, cancellationToken);
    }

    /// <summary>Records the Proof of delivery, which the TMS must accept, and returns what it answered.</summary>
    public static async Task<JsonNode> RecordedProofAsync(
        this TmsFixture tms, JsonObject metadata, IReadOnlyList<SentFile> files, CancellationToken cancellationToken, string? correlationId = null)
    {
        using var response = await tms.RecordProofAsync(metadata, files, cancellationToken, correlationId);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK, body);
        return JsonNode.Parse(body)!;
    }

    /// <summary>Records the Proof of delivery, which the TMS must refuse with the status, and returns its problem.</summary>
    public static async Task<JsonNode> RecordProofRefusedAsync(
        this TmsFixture tms, JsonObject metadata, IReadOnlyList<SentFile> files, HttpStatusCode status, CancellationToken cancellationToken, string? correlationId = null)
    {
        using var response = await tms.RecordProofAsync(metadata, files, cancellationToken, correlationId);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        response.StatusCode.Should().Be(status, body);
        return JsonNode.Parse(body)!;
    }

    /// <summary>A Carrier Shipment the Carrier delivered: Booked, then Delivered by a Tracking event, with the Carrier's own booking reference.</summary>
    public static async Task<(string ShipmentNumber, string CarrierBookingReference)> DeliveredShipmentAsync(
        this TmsFixture tms, CancellationToken cancellationToken)
    {
        var (shipmentNumber, reference) = await tms.BookedShipmentAsync(cancellationToken);
        var delivered = await tms.RecordedAsync(TrackingEventCalls.Event("Delivered", reference, DateTimeOffset.UtcNow.AddMinutes(-1)), cancellationToken);
        delivered["shipmentStatus"]!.GetValue<string>().Should().Be("Delivered");
        return (shipmentNumber, reference);
    }

    /// <summary>A Shipment's current Proof of delivery, from the API; the response is the caller's to check.</summary>
    public static async Task<HttpResponseMessage> GetProofAsync(this TmsFixture tms, string shipmentNumber, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(tms);
        return await tms.Tms.GetAsync(new Uri($"/v1/shipments/{shipmentNumber}/proof-of-delivery", UriKind.Relative), cancellationToken);
    }

    /// <summary>A file of a Shipment's current Proof of delivery, from the API; the response is the caller's to check.</summary>
    public static async Task<HttpResponseMessage> GetProofFileAsync(this TmsFixture tms, string shipmentNumber, string fileId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(tms);
        return await tms.Tms.GetAsync(new Uri($"/v1/shipments/{shipmentNumber}/proof-of-delivery/files/{fileId}", UriKind.Relative), cancellationToken);
    }

    /// <summary>Every Proof of delivery of a Shipment, current and superseded, for its timeline, from the API.</summary>
    public static async Task<JsonArray> GetProofsOnTimelineAsync(this TmsFixture tms, string shipmentNumber, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(tms);
        using var response = await tms.Tms.GetAsync(new Uri($"/v1/shipments/{shipmentNumber}/proofs-of-delivery", UriKind.Relative), cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK, body);
        return JsonNode.Parse(body)!.AsArray();
    }

    /// <summary>The Delivered Shipments without a current Proof of delivery, from the API.</summary>
    public static async Task<JsonArray> ListDeliveredWithoutProofAsync(this TmsFixture tms, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(tms);
        using var response = await tms.Tms.GetAsync(new Uri("/v1/shipments/delivered-without-proof-of-delivery", UriKind.Relative), cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK, body);
        return JsonNode.Parse(body)!.AsArray();
    }
}
