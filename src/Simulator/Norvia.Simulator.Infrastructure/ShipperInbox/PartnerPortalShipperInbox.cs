using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Norvia.Simulator.Application.Ports;

namespace Norvia.Simulator.Infrastructure.ShipperInbox;

/// <summary>Reads a Shipper Persona's invoices inbox from the Partner portal, with the Shipper's own endpoint key. It changes nothing.</summary>
internal sealed partial class PartnerPortalShipperInbox(HttpClient http, IOptions<ShipperInboxOptions> options, ILogger<PartnerPortalShipperInbox> logger) : IShipperInbox
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<IReadOnlyList<InboxDocument>?> ListAsync(string shipperId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(shipperId);

        var settings = options.Value;
        if (settings.BaseAddress is not { } baseAddress || !settings.ShipperEndpointKeys.TryGetValue(shipperId, out var key) || string.IsNullOrWhiteSpace(key))
        {
            LogNotConfigured(shipperId);
            return null;
        }

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(baseAddress, $"/shipper/v1/{Uri.EscapeDataString(shipperId)}/invoices"));
            request.Headers.Add(ShipperInboxOptions.ApiKeyHeader, key);
            using var response = await http.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                LogUnavailable(shipperId, (int)response.StatusCode);
                return null;
            }

            var documents = await response.Content.ReadFromJsonAsync<List<Document>>(Json, cancellationToken);
            return documents?.Select(document => new InboxDocument(document.DocumentNumber ?? string.Empty, document.DocumentType ?? string.Empty, document.CorrelationId)).ToList();
        }
        catch (Exception exception) when (exception is HttpRequestException or JsonException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            LogNotRead(exception, shipperId);
            return null;
        }
    }

    private sealed record Document(string? DocumentNumber, string? DocumentType, string? CorrelationId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The Partner portal's address or the endpoint key of Shipper {ShipperId} isn't configured, so its invoices inbox isn't read")]
    private partial void LogNotConfigured(string shipperId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The Partner portal answered the invoices inbox of {ShipperId} with {StatusCode}")]
    private partial void LogUnavailable(string shipperId, int statusCode);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The invoices inbox of {ShipperId} couldn't be read from the Partner portal")]
    private partial void LogNotRead(Exception exception, string shipperId);
}
