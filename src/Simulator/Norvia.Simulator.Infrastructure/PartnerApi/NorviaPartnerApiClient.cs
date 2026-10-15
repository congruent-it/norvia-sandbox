using System.Diagnostics;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Norvia.Simulator.Application.Ports;
using Norvia.Simulator.Domain.Carriers;
using Norvia.Simulator.Domain.Log;
using Norvia.Simulator.Domain.Plans;
using Norvia.Simulator.Domain.Worlds;

namespace Norvia.Simulator.Infrastructure.PartnerApi;

/// <summary>
/// Books through the Norvia Partner API as the Shipper's Persona: its subscription key names the product, its token says
/// which party it is (ADR 0005). A booking never names the party. Each is sent in a span of its own, named
/// <c>Send booking</c> like the Partner portal's, which is the Simulator's hop on the Message trail; once the answer is in it
/// is logged with the tracking keys it knows (coding guidelines §7). A Carrier answers, tracks and proves delivery the same way, as
/// its own Persona, each call in a span named for what it does (<see cref="CarrierActions"/>).
/// </summary>
internal sealed partial class NorviaPartnerApiClient(
    HttpClient http,
    IPersonaTokens tokens,
    IOptions<PartnerApiOptions> options,
    SimulatorActivities activities,
    TimeProvider time,
    ILogger<NorviaPartnerApiClient> logger)
    : IPartnerApi
{
    public async Task<BookingAnswer> SendBookingAsync(PlannedBooking booking, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(booking);

        using var span = activities.Source.StartActivity("Send booking", ActivityKind.Internal);
        BookingAnswer answer;
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, new Uri("transport-orders", UriKind.Relative))
            {
                Content = JsonContent.Create(PartnerBookingFormat.ToJson(booking, time.GetUtcNow())),
            };
            // Buffered, so it goes out with its length: the intake Logic App's trigger gets no body from a chunked one.
            await request.Content.LoadIntoBufferAsync(cancellationToken);
            request.Headers.Add("Ocp-Apim-Subscription-Key", options.Value.SubscriptionKeyOf(booking.PersonaId));
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await tokens.GetTokenAsync(booking.PersonaId, cancellationToken));

            using var response = await http.SendAsync(request, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            answer = BookingAnswers.Read((int)response.StatusCode, body, CorrelationIdOf(response));
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            // Unreachable, too slow, or no token to call with: nothing reached Norvia, so nothing was booked.
            LogNotSent(exception, booking.PersonaId);
            answer = BookingAnswers.NotReceived(correlationId: null, "The booking didn't reach the Norvia Partner API.");
        }

        using (logger.BeginScope(TrackingKeysOf(booking, answer)))
        {
            LogAnswered(booking.PersonaId, booking.Reference, answer.Outcome);
        }

        return answer;
    }

    public Task<CarrierCallAnswer> AnswerBookingAsync(SimulatedCarrier carrier, CarrierAnswerRequest answer, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(answer);
        return SendCarrierAsync(
            carrier, CarrierActions.Answer, "carrier-answers", "carrier-answer", () => CarrierFormats.Answer(answer), answer.CarrierBookingNumber,
            (status, body, correlation) => CarrierAnswers.Answered(status, body, correlation), cancellationToken);
    }

    public Task<CarrierCallAnswer> SendTrackingAsync(SimulatedCarrier carrier, CarrierTrackingRequest tracking, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(tracking);
        return SendCarrierAsync(
            carrier, CarrierActions.Track, "carrier-tracking", "carrier-tracking", () => CarrierFormats.Tracking(tracking), tracking.CarrierBookingNumber,
            (status, body, correlation) => CarrierAnswers.Accepted(status, body, correlation, LogOutcome.TrackingSent), cancellationToken);
    }

    public Task<CarrierCallAnswer> SendProofOfDeliveryAsync(SimulatedCarrier carrier, CarrierProofRequest proof, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(proof);
        return SendCarrierAsync(
            carrier, CarrierActions.ProveDelivery, "carrier-proofs-of-delivery", "carrier-proof-of-delivery", () => CarrierFormats.Proof(proof), proof.CarrierBookingNumber,
            (status, body, correlation) => CarrierAnswers.Accepted(status, body, correlation, LogOutcome.ProofOfDeliverySent), cancellationToken);
    }

    private async Task<CarrierCallAnswer> SendCarrierAsync(
        SimulatedCarrier carrier, string action, string path, string flow, Func<JsonObject> format, string carrierBookingNumber,
        Func<int, string, string?, CarrierCallAnswer> read, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(carrier);
        var personaId = carrier.PersonaId ?? throw new InvalidOperationException($"Carrier {carrier.Id} has no Persona to call Norvia as.");

        using var span = activities.Source.StartActivity(action, ActivityKind.Internal);
        CarrierCallAnswer answer;
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(path, UriKind.Relative)) { Content = JsonContent.Create(format()) };
            // Buffered, so it goes out with its length, as a booking does.
            await request.Content.LoadIntoBufferAsync(cancellationToken);
            request.Headers.Add("Ocp-Apim-Subscription-Key", options.Value.SubscriptionKeyOf(personaId));
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await tokens.GetTokenAsync(personaId, cancellationToken));

            using var response = await http.SendAsync(request, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            answer = read((int)response.StatusCode, body, CorrelationIdOf(response));
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            // Unreachable, too slow, or no token to call with: nothing reached Norvia.
            LogCarrierNotSent(exception, action, personaId);
            answer = CarrierAnswers.NotReceived("The call didn't reach the Norvia Partner API.");
        }

        using (logger.BeginScope(CarrierTrackingKeysOf(flow, carrier.Id, carrierBookingNumber, answer)))
        {
            LogCarrierAnswered(action, personaId, answer.Outcome);
        }

        return answer;
    }

    /// <summary>
    /// The tracking contract's keys the Simulator knows of a Carrier's call (coding guidelines §7), so its trace joins Norvia's
    /// Message trail. A key that doesn't apply is left out, never sent empty.
    /// </summary>
    internal static Dictionary<string, object?> CarrierTrackingKeysOf(string flow, string carrierId, string carrierBookingNumber, CarrierCallAnswer answer)
    {
        ArgumentNullException.ThrowIfNull(answer);

        var keys = new Dictionary<string, object?> { ["flow"] = flow, ["partyId"] = carrierId, ["carrierBookingNumber"] = carrierBookingNumber };
        switch (answer.Outcome)
        {
            case LogOutcome.Confirmed or LogOutcome.Declined or LogOutcome.TrackingSent or LogOutcome.ProofOfDeliverySent:
                keys["outcome"] = "Succeeded";
                break;
            case LogOutcome.CarrierRefused:
                keys["outcome"] = "Refused";
                break;
            default:
                keys["outcome"] = "Failed";
                keys["failureCode"] = "CarrierCallNotReceived";
                break;
        }

        if (answer.CorrelationId is { } correlationId)
        {
            keys["correlationId"] = correlationId;
        }

        return keys;
    }

    /// <summary>
    /// The tracking contract's keys the Simulator knows (coding guidelines §7), so its trace joins Norvia's Message trail.
    /// Never what was booked: that holds the Consignee's personal data.
    /// </summary>
    internal static Dictionary<string, object?> TrackingKeysOf(PlannedBooking booking, BookingAnswer answer)
    {
        var keys = new Dictionary<string, object?> { ["flow"] = "transport-order-intake", ["partyId"] = booking.ShipperId };
        foreach (var (key, value) in OutcomeOf(answer.Outcome))
        {
            keys[key] = value;
        }

        if (answer.CorrelationId is { } correlationId)
        {
            keys["correlationId"] = correlationId;
        }

        if (answer.TransportOrderNumber is { } number)
        {
            keys["transportOrderNumber"] = number;
        }

        return keys;
    }

    private static Dictionary<string, object?> OutcomeOf(LogOutcome outcome) => outcome switch
    {
        LogOutcome.Accepted or LogOutcome.AlreadyBooked => new() { ["outcome"] = "Succeeded" },
        LogOutcome.Rejected => new() { ["outcome"] = "Refused", ["failureCode"] = "TransportOrderRejected" },
        LogOutcome.Refused => new() { ["outcome"] = "Refused" },
        LogOutcome.NotReceived => new() { ["outcome"] = "Failed", ["failureCode"] = "BookingNotReceived" },
        _ => throw new ArgumentOutOfRangeException(nameof(outcome), outcome, "Every answer records its outcome."),
    };

    private static string? CorrelationIdOf(HttpResponseMessage response) =>
        response.Headers.TryGetValues("X-Correlation-ID", out var values) ? values.FirstOrDefault() : null;

    [LoggerMessage(Level = LogLevel.Warning, Message = "{Action} by Persona {PersonaId} didn't reach the Norvia Partner API")]
    private partial void LogCarrierNotSent(Exception exception, string action, string personaId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Norvia answered {Action} by Persona {PersonaId}: {Outcome}")]
    private partial void LogCarrierAnswered(string action, string personaId, LogOutcome outcome);

    [LoggerMessage(Level = LogLevel.Warning, Message = "A booking by Persona {PersonaId} didn't reach the Norvia Partner API")]
    private partial void LogNotSent(Exception exception, string personaId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Norvia answered booking {ShipperReference} by Persona {PersonaId}: {Outcome}")]
    private partial void LogAnswered(string personaId, string shipperReference, LogOutcome outcome);
}
