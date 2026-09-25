using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Norvia.PartnerPortal.Application.Bookings;
using Norvia.PartnerPortal.Domain.Bookings;
using Norvia.PartnerPortal.Domain.Personas;

namespace Norvia.PartnerPortal.Infrastructure.PartnerApi;

/// <summary>
/// Books through the Norvia Partner API as a Persona: its subscription key names the product, its token says which
/// party it is (ADR 0005). The booking itself never names the party.
/// </summary>
internal sealed partial class NorviaPartnerApiClient(
    HttpClient http,
    IPersonaTokens tokens,
    IOptions<PartnerApiOptions> options,
    ILogger<NorviaPartnerApiClient> logger)
    : INorviaPartnerApi
{
    public async Task<NorviaAnswer> SubmitAsync(Persona persona, BookingRequest booking, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(persona);

        NorviaAnswer answer;
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, new Uri("transport-orders", UriKind.Relative))
            {
                Content = JsonContent.Create(PartnerBookingFormat.ToJson(booking)),
            };
            request.Headers.Add("Ocp-Apim-Subscription-Key", options.Value.SubscriptionKeyOf(persona.Id));
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await tokens.GetTokenAsync(persona.Id, cancellationToken));

            using var response = await http.SendAsync(request, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            answer = PartnerAnswers.Read((int)response.StatusCode, body, CorrelationIdOf(response));
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            // Unreachable, too slow, or no token to call with: nothing reached Norvia, so nothing was booked.
            LogNotSent(exception, persona.Id.Value);
            answer = NorviaAnswer.NotReceived(messageId: null);
        }

        using (logger.BeginScope(TrackingKeysOf(persona, answer)))
        {
            LogAnswered(persona.Id.Value, answer.Result);
        }

        return answer;
    }

    /// <summary>
    /// The tracking contract's keys the portal knows (coding guidelines §7), so its trace joins Norvia's Message trail.
    /// Never what was booked: that holds the Consignee's personal data.
    /// </summary>
    private static Dictionary<string, object?> TrackingKeysOf(Persona persona, NorviaAnswer answer)
    {
        var keys = new Dictionary<string, object?> { ["flow"] = "transport-order-intake", ["partyId"] = persona.PartyId };
        if (answer.MessageId is { } correlationId)
        {
            keys["correlationId"] = correlationId;
        }

        if (answer.TransportOrderNumber is { } number)
        {
            keys["transportOrderNumber"] = number;
        }

        return keys;
    }

    private static string? CorrelationIdOf(HttpResponseMessage response) =>
        response.Headers.TryGetValues("X-Correlation-ID", out var values) ? values.FirstOrDefault() : null;

    [LoggerMessage(Level = LogLevel.Warning, Message = "A booking by Persona {PersonaId} didn't reach the Norvia Partner API")]
    private partial void LogNotSent(Exception exception, string personaId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Norvia answered a booking by Persona {PersonaId}: {Result}")]
    private partial void LogAnswered(string personaId, BookingResult result);
}
