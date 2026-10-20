using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Norvia.Simulator.Application.Ports;
using Norvia.Simulator.Infrastructure.ShipperInbox;

namespace Norvia.Simulator.Infrastructure.CarrierInbox;

/// <summary>
/// Reads a Carrier Persona's booking inbox and invoices page from the Partner portal, with the Carrier's own endpoint key (the one its booking endpoint asks, which
/// the portal's reads ask too). It changes nothing.
/// </summary>
internal sealed partial class PartnerPortalCarrierInbox(HttpClient http, IOptions<ShipperInboxOptions> options, ILogger<PartnerPortalCarrierInbox> logger) : ICarrierInbox
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<IReadOnlyDictionary<string, decimal>?> AgreedPricesAsync(string carrierId, CancellationToken cancellationToken)
    {
        var bookings = await ReadAsync<List<Booking>>(carrierId, "bookings", cancellationToken);
        return bookings?
            .Where(booking => !string.IsNullOrWhiteSpace(booking.CarrierBookingNumber) && booking.AgreedAmount is not null)
            .GroupBy(booking => booking.CarrierBookingNumber!)
            .ToDictionary(group => group.Key, group => group.First().AgreedAmount!.Value);
    }

    public async Task<IReadOnlyList<CarrierInvoiceOutcome>?> OutcomesAsync(string carrierId, CancellationToken cancellationToken)
    {
        var outcomes = await ReadAsync<List<Outcome>>(carrierId, "invoice-outcomes", cancellationToken);
        return outcomes?
            .Select(outcome => new CarrierInvoiceOutcome(
                outcome.InvoiceNo ?? string.Empty, outcome.CorrelationId ?? string.Empty, outcome.ApprovedAmount,
                [.. (outcome.Lines ?? []).Select(line => new CarrierOutcomeLine(line.BookingRef ?? string.Empty, line.Result ?? string.Empty, line.AcceptedAmount, line.Remark))]))
            .ToList();
    }

    private async Task<T?> ReadAsync<T>(string carrierId, string resource, CancellationToken cancellationToken)
        where T : class
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(carrierId);

        var settings = options.Value;
        if (settings.BaseAddress is not { } baseAddress || !settings.CarrierEndpointKeys.TryGetValue(carrierId, out var key) || string.IsNullOrWhiteSpace(key))
        {
            LogNotConfigured(carrierId);
            return null;
        }

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(baseAddress, $"/carrier/v1/{Uri.EscapeDataString(carrierId)}/{resource}"));
            request.Headers.Add(ShipperInboxOptions.ApiKeyHeader, key);
            using var response = await http.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                LogUnavailable(carrierId, resource, (int)response.StatusCode);
                return null;
            }

            return await response.Content.ReadFromJsonAsync<T>(Json, cancellationToken);
        }
        catch (Exception exception) when (exception is HttpRequestException or JsonException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            LogNotRead(exception, carrierId, resource);
            return null;
        }
    }

    private sealed record Booking(string? CarrierBookingNumber, decimal? AgreedAmount);

    private sealed record Outcome(string? InvoiceNo, string? CorrelationId, decimal ApprovedAmount, List<OutcomeLine>? Lines);

    private sealed record OutcomeLine(string? BookingRef, string? Result, decimal AcceptedAmount, string? Remark);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The Partner portal's address or the endpoint key of Carrier {CarrierId} isn't configured, so its inbox isn't read")]
    private partial void LogNotConfigured(string carrierId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The Partner portal answered the {Resource} of Carrier {CarrierId} with {StatusCode}")]
    private partial void LogUnavailable(string carrierId, string resource, int statusCode);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The {Resource} of Carrier {CarrierId} couldn't be read from the Partner portal")]
    private partial void LogNotRead(Exception exception, string carrierId, string resource);
}
