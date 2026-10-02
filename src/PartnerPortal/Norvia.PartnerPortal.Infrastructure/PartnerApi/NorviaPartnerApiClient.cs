using System.Diagnostics;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Norvia.PartnerPortal.Application.Bookings;
using Norvia.PartnerPortal.Domain.Bookings;
using Norvia.PartnerPortal.Domain.CarrierBookings;
using Norvia.PartnerPortal.Domain.Personas;

namespace Norvia.PartnerPortal.Infrastructure.PartnerApi;

/// <summary>
/// Books and answers through the Norvia Partner API as a Persona: its subscription key names the product, its token says
/// which party it is (ADR 0005). A booking or an answer itself never names the party. It sends each in a span of its
/// own, which the Integration console names the portal's hop after.
/// </summary>
internal sealed partial class NorviaPartnerApiClient(
    HttpClient http,
    IPersonaTokens tokens,
    IOptions<PartnerApiOptions> options,
    PortalActivities activities,
    ILogger<NorviaPartnerApiClient> logger)
    : INorviaPartnerApi
{
    public async Task<NorviaAnswer> SubmitAsync(Persona persona, BookingRequest booking, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(persona);

        using var span = activities.Source.StartActivity("Send booking", ActivityKind.Internal);
        NorviaAnswer answer;
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, new Uri("transport-orders", UriKind.Relative))
            {
                Content = JsonContent.Create(PartnerBookingFormat.ToJson(booking)),
            };
            // Buffered, so it goes out with its length: the intake Logic App's trigger gets no body from a chunked one.
            await request.Content.LoadIntoBufferAsync(cancellationToken);
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

    public async Task<NorviaReply> AnswerAsync(
        Persona persona, string carrierBookingNumber, string shipmentNumber, CarrierAnswer answer, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(persona);

        using var span = activities.Source.StartActivity("Send Carrier answer", ActivityKind.Internal);
        NorviaReply reply;
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, new Uri("carrier-answers", UriKind.Relative))
            {
                Content = JsonContent.Create(CarrierAnswerFormat.ToJson(carrierBookingNumber, answer)),
            };
            // Buffered, so it goes out with its length: the Carrier-answer Logic App's trigger gets no body from a chunked one.
            await request.Content.LoadIntoBufferAsync(cancellationToken);
            request.Headers.Add("Ocp-Apim-Subscription-Key", options.Value.SubscriptionKeyOf(persona.Id));
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await tokens.GetTokenAsync(persona.Id, cancellationToken));

            using var response = await http.SendAsync(request, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            reply = PartnerAnswers.ReadReply((int)response.StatusCode, body, CorrelationIdOf(response));
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            // Unreachable, too slow, or no token to call with: nothing reached Norvia, so nothing was recorded.
            LogAnswerNotSent(exception, persona.Id.Value, carrierBookingNumber);
            reply = NorviaReply.NotReceived(messageId: null);
        }

        using (logger.BeginScope(TrackingKeysOf(persona, carrierBookingNumber, shipmentNumber, reply)))
        {
            LogAnswerReplied(persona.Id.Value, carrierBookingNumber, reply.Result);
        }

        return reply;
    }

    /// <summary>
    /// The tracking contract's keys the portal knows of an answer (coding guidelines §7), so its trace joins Norvia's Message
    /// trail by the answer's correlation ID, the Carrier booking number and the Shipment number. Never what the Carrier
    /// said: that is its own business.
    /// </summary>
    private static Dictionary<string, object?> TrackingKeysOf(Persona persona, string carrierBookingNumber, string shipmentNumber, NorviaReply reply)
    {
        var keys = new Dictionary<string, object?>
        {
            ["flow"] = "carrier-answer",
            ["partyId"] = persona.PartyId,
            ["carrierBookingNumber"] = carrierBookingNumber,
            ["shipmentNumber"] = shipmentNumber,
        };
        foreach (var (key, value) in OutcomeOf(reply.Result))
        {
            keys[key] = value;
        }

        if (reply.MessageId is { } correlationId)
        {
            keys["correlationId"] = correlationId;
        }

        return keys;
    }

    private static Dictionary<string, object?> OutcomeOf(CarrierAnswerResult result) => result switch
    {
        CarrierAnswerResult.Recorded => new() { ["outcome"] = "Succeeded" },
        CarrierAnswerResult.BookingNotOpen => new() { ["outcome"] = "Refused", ["failureCode"] = "BookingNotOpen" },
        CarrierAnswerResult.BookingNotFound => new() { ["outcome"] = "Refused", ["failureCode"] = "BookingNotFound" },
        CarrierAnswerResult.NotAllowed => new() { ["outcome"] = "Refused", ["failureCode"] = "CarrierNotAllowed" },
        CarrierAnswerResult.Invalid => new() { ["outcome"] = "Refused", ["failureCode"] = "CarrierAnswerUnreadable" },
        CarrierAnswerResult.Refused => new() { ["outcome"] = "Refused" },
        CarrierAnswerResult.NotReceived => new() { ["outcome"] = "Failed", ["failureCode"] = "AnswerNotReceived" },
        _ => throw new ArgumentOutOfRangeException(nameof(result), result, "Every reply records its outcome."),
    };

    /// <summary>
    /// The tracking contract's keys the portal knows (coding guidelines §7), so its trace joins Norvia's Message trail.
    /// Never what was booked: that holds the Consignee's personal data. Its outcome reads Norvia's answer as the intake's
    /// other hops read the TMS's.
    /// </summary>
    private static Dictionary<string, object?> TrackingKeysOf(Persona persona, NorviaAnswer answer)
    {
        var keys = new Dictionary<string, object?> { ["flow"] = "transport-order-intake", ["partyId"] = persona.PartyId };
        foreach (var (key, value) in OutcomeOf(answer.Result))
        {
            keys[key] = value;
        }

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

    private static Dictionary<string, object?> OutcomeOf(BookingResult result) => result switch
    {
        BookingResult.Accepted or BookingResult.AlreadyBooked => new() { ["outcome"] = "Succeeded" },
        BookingResult.Rejected => new() { ["outcome"] = "Refused", ["failureCode"] = "TransportOrderRejected" },
        BookingResult.Refused => new() { ["outcome"] = "Refused" },
        BookingResult.NotReceived => new() { ["outcome"] = "Failed", ["failureCode"] = "BookingNotReceived" },
        _ => throw new ArgumentOutOfRangeException(nameof(result), result, "Every answer records its outcome."),
    };

    private static string? CorrelationIdOf(HttpResponseMessage response) =>
        response.Headers.TryGetValues("X-Correlation-ID", out var values) ? values.FirstOrDefault() : null;

    [LoggerMessage(Level = LogLevel.Warning, Message = "A booking by Persona {PersonaId} didn't reach the Norvia Partner API")]
    private partial void LogNotSent(Exception exception, string personaId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Norvia answered a booking by Persona {PersonaId}: {Result}")]
    private partial void LogAnswered(string personaId, BookingResult result);

    [LoggerMessage(Level = LogLevel.Warning, Message = "An answer by Persona {PersonaId} to {CarrierBookingNumber} didn't reach the Norvia Partner API")]
    private partial void LogAnswerNotSent(Exception exception, string personaId, string carrierBookingNumber);

    [LoggerMessage(Level = LogLevel.Information, Message = "Norvia replied to an answer by Persona {PersonaId} to {CarrierBookingNumber}: {Result}")]
    private partial void LogAnswerReplied(string personaId, string carrierBookingNumber, CarrierAnswerResult result);
}
