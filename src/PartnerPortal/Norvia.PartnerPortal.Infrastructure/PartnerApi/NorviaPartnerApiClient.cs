using System.Diagnostics;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Norvia.PartnerPortal.Application.Bookings;
using Norvia.PartnerPortal.Application.Bookings.DraftFromText;
using Norvia.PartnerPortal.Application.CarrierBookings.Answer;
using Norvia.PartnerPortal.Application.CarrierInvoices;
using Norvia.PartnerPortal.Application.CarrierInvoices.Submit;
using Norvia.PartnerPortal.Application.CarrierShipments;
using Norvia.PartnerPortal.Application.CarrierShipments.SendProofOfDelivery;
using Norvia.PartnerPortal.Application.Faults;
using Norvia.PartnerPortal.Application.ShipperTracking;
using Norvia.PartnerPortal.Domain.Bookings;
using Norvia.PartnerPortal.Domain.CarrierBookings;
using Norvia.PartnerPortal.Domain.CarrierInvoices;
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
    IPartnerPortalFaults faults,
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

    public async Task<BookingDraftAnswer> DraftBookingFromTextAsync(Persona persona, string text, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(persona);
        ArgumentNullException.ThrowIfNull(text);

        using var span = activities.Source.StartActivity("Draft booking from text", ActivityKind.Internal);
        BookingDraftAnswer answer;
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, new Uri("booking-drafts", UriKind.Relative))
            {
                Content = JsonContent.Create(new { text }),
            };
            request.Headers.Add("Ocp-Apim-Subscription-Key", options.Value.SubscriptionKeyOf(persona.Id));
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await tokens.GetTokenAsync(persona.Id, cancellationToken));

            using var response = await http.SendAsync(request, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            answer = PartnerBookingDraftFormat.Read((int)response.StatusCode, body, CorrelationIdOf(response));
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            // Unreachable, too slow, or no token to call with. Only the exception's type is logged: never the text.
            LogDraftNotAsked(persona.Id.Value, exception.GetType().Name);
            answer = BookingDraftAnswer.Refused(BookingDraftOutcome.Unavailable, messageId: null);
        }

        var draftKeys = new Dictionary<string, object?> { ["flow"] = "booking-draft", ["partyId"] = persona.PartyId, ["outcome"] = answer.Outcome == BookingDraftOutcome.Drafted ? "Succeeded" : "Refused" };
        if (answer.MessageId is { } draftCorrelationId)
        {
            draftKeys["correlationId"] = draftCorrelationId;
        }

        using (logger.BeginScope(draftKeys))
        {
            LogDraftAnswered(persona.Id.Value, answer.Outcome);
        }

        return answer;
    }

    public async Task<NorviaReply> AnswerAsync(
        Persona persona, string carrierBookingNumber, string shipmentNumber, CarrierAnswer answer, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(persona);

        using var span = activities.Source.StartActivity("Send Carrier answer", ActivityKind.Internal);
        // The fault only changes a rejection: a confirmation has no reason code.
        var unmappedReason = faults.AnswerUnmappedReason && answer is CarrierAnswer.Rejection;
        NorviaReply reply;
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, new Uri("carrier-answers", UriKind.Relative))
            {
                Content = JsonContent.Create(CarrierAnswerFormat.ToJson(carrierBookingNumber, answer, unmappedReason)),
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

        var answerKeys = TrackingKeysOf(persona, carrierBookingNumber, shipmentNumber, reply);
        if (unmappedReason)
        {
            answerKeys["fault"] = PartnerPortalFaultNames.AnswerUnmappedReason;
        }

        using (logger.BeginScope(answerKeys))
        {
            LogAnswerReplied(persona.Id.Value, carrierBookingNumber, reply.Result);
        }

        return reply;
    }

    public async Task<TrackingReply> SubmitTrackingAsync(
        Persona persona, string shipmentNumber, CarrierTrackingEvent trackingEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(persona);
        ArgumentNullException.ThrowIfNull(trackingEvent);

        using var span = activities.Source.StartActivity("Send Tracking event", ActivityKind.Internal);
        var unmappedCode = faults.TrackingUnmappedCode && CarrierTrackingFormat.IsUnmappable(trackingEvent.Type);
        TrackingReply reply;
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, new Uri("carrier-tracking", UriKind.Relative))
            {
                Content = JsonContent.Create(CarrierTrackingFormat.ToJson(trackingEvent, unmappedCode)),
            };
            request.Headers.Add("Ocp-Apim-Subscription-Key", options.Value.SubscriptionKeyOf(persona.Id));
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await tokens.GetTokenAsync(persona.Id, cancellationToken));

            using var response = await http.SendAsync(request, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            reply = PartnerAnswers.ReadTrackingReply((int)response.StatusCode, body, CorrelationIdOf(response));
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            // Unreachable, too slow, or no token to call with: nothing reached Norvia, so nothing was sent.
            LogTrackingNotSent(exception, persona.Id.Value, trackingEvent.CarrierBookingNumber);
            reply = TrackingReply.NotReceived(messageId: null);
        }

        var keys = TrackingEventTracking.KeysOf(persona, shipmentNumber, trackingEvent, reply.MessageId);
        if (unmappedCode)
        {
            keys["fault"] = PartnerPortalFaultNames.TrackingUnmappedCode;
        }

        foreach (var (key, value) in TrackingEventTracking.OutcomeOf(reply.Result))
        {
            keys[key] = value;
        }

        using (logger.BeginScope(keys))
        {
            LogTrackingReplied(persona.Id.Value, trackingEvent.CarrierBookingNumber, reply.Result);
        }

        return reply;
    }

    public async Task<TrackingReply> SubmitProofOfDeliveryAsync(
        Persona persona, string shipmentNumber, CarrierProofOfDelivery proof, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(persona);
        ArgumentNullException.ThrowIfNull(proof);

        using var span = activities.Source.StartActivity("Send Proof of delivery", ActivityKind.Internal);
        var heicPhoto = faults.ProofHeicPhoto;
        TrackingReply reply;
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, new Uri("carrier-proofs-of-delivery", UriKind.Relative))
            {
                Content = JsonContent.Create(CarrierProofOfDeliveryFormat.ToJson(proof, heicPhoto)),
            };
            // Buffered, so it goes out with its length: the edge reads the body whole, and refuses one over its limit by that length.
            await request.Content.LoadIntoBufferAsync(cancellationToken);
            request.Headers.Add("Ocp-Apim-Subscription-Key", options.Value.SubscriptionKeyOf(persona.Id));
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await tokens.GetTokenAsync(persona.Id, cancellationToken));

            using var response = await http.SendAsync(request, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            reply = PartnerAnswers.ReadProofOfDeliveryReply((int)response.StatusCode, body, CorrelationIdOf(response));
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            // Unreachable, too slow, or no token to call with: nothing reached Norvia, so nothing was sent.
            LogProofOfDeliveryNotSent(exception, persona.Id.Value, proof.CarrierBookingNumber);
            reply = TrackingReply.NotReceived(messageId: null);
        }

        var keys = ProofOfDeliveryTracking.KeysOf(persona, shipmentNumber, proof, reply.MessageId);
        if (heicPhoto)
        {
            keys["fault"] = PartnerPortalFaultNames.ProofHeicPhoto;
        }

        foreach (var (key, value) in ProofOfDeliveryTracking.OutcomeOf(reply.Result))
        {
            keys[key] = value;
        }

        using (logger.BeginScope(keys))
        {
            LogProofOfDeliveryReplied(persona.Id.Value, proof.CarrierBookingNumber, reply.Result);
        }

        return reply;
    }

    public async Task<CarrierInvoiceReply> SubmitCarrierInvoiceAsync(
        Persona persona, string invoiceNumber, DateOnly invoiceDate, IReadOnlyList<CarrierInvoiceLineDraft> lines, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(persona);

        using var span = activities.Source.StartActivity("Send Carrier invoice", ActivityKind.Internal);
        CarrierInvoiceReply reply;
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, new Uri("carrier-invoices", UriKind.Relative))
            {
                Content = JsonContent.Create(CarrierInvoiceFormat.ToJson(invoiceNumber, invoiceDate, lines)),
            };
            // Buffered, so it goes out with its length: the Carrier invoice Logic App's trigger gets no body from a chunked one.
            await request.Content.LoadIntoBufferAsync(cancellationToken);
            request.Headers.Add("Ocp-Apim-Subscription-Key", options.Value.SubscriptionKeyOf(persona.Id));
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await tokens.GetTokenAsync(persona.Id, cancellationToken));

            using var response = await http.SendAsync(request, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            reply = PartnerAnswers.ReadCarrierInvoiceReply((int)response.StatusCode, body, CorrelationIdOf(response));
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            // Unreachable, too slow, or no token to call with: nothing reached Norvia, so nothing was recorded.
            LogCarrierInvoiceNotSent(exception, persona.Id.Value, invoiceNumber);
            reply = CarrierInvoiceReply.NotReceived(messageId: null);
        }

        var keys = CarrierInvoiceTracking.KeysOf(persona, invoiceNumber, reply.MessageId);
        foreach (var (key, value) in CarrierInvoiceTracking.OutcomeOf(reply.Result))
        {
            keys[key] = value;
        }

        using (logger.BeginScope(keys))
        {
            LogCarrierInvoiceReplied(persona.Id.Value, invoiceNumber, reply.Result);
        }

        return reply;
    }

    public Task<TrackingAnswer<IReadOnlyList<ShipperTrackingSummary>>> ListTransportOrdersAsync(
        Persona persona, ShipperTrackingFilter filter, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(filter);
        return QueryTrackingAsync(
            persona, "List Transport Orders", transportOrderNumber: null, $"transport-orders?{ShipperTrackingFormat.QueryOf(filter)}",
            async (response, token) => ShipperTrackingFormat.ReadList(await response.Content.ReadAsStringAsync(token)), cancellationToken);
    }

    public Task<TrackingAnswer<ShipperTrackingDetail>> GetTransportOrderAsync(Persona persona, string transportOrderNumber, CancellationToken cancellationToken) =>
        QueryTrackingAsync(
            persona, "Get Transport Order", transportOrderNumber, $"transport-orders/{Uri.EscapeDataString(transportOrderNumber)}",
            ReadDetailAsync, cancellationToken);

    public Task<TrackingAnswer<ShipperTrackingDetail>> FindTransportOrderAsync(Persona persona, string shipperReference, CancellationToken cancellationToken) =>
        QueryTrackingAsync(
            persona, "Find Transport Order", transportOrderNumber: null, $"transport-orders/find?shipperReference={Uri.EscapeDataString(shipperReference)}",
            ReadDetailAsync, cancellationToken);

    public Task<TrackingAnswer<ShipperProofFileContent>> DownloadProofOfDeliveryFileAsync(
        Persona persona, string transportOrderNumber, Guid fileId, CancellationToken cancellationToken) =>
        QueryTrackingAsync(
            persona, "Download Proof of delivery file", transportOrderNumber,
            $"transport-orders/{Uri.EscapeDataString(transportOrderNumber)}/proof-of-delivery/files/{fileId}",
            async (response, token) => new ShipperProofFileContent(
                response.Content.Headers.ContentDisposition?.FileNameStar ?? response.Content.Headers.ContentDisposition?.FileName?.Trim('"') ?? fileId.ToString(),
                response.Content.Headers.ContentType?.MediaType ?? "application/octet-stream",
                await response.Content.ReadAsByteArrayAsync(token)),
            cancellationToken);

    private static async Task<ShipperTrackingDetail> ReadDetailAsync(HttpResponseMessage response, CancellationToken cancellationToken) =>
        ShipperTrackingFormat.ReadDetail(await response.Content.ReadAsStringAsync(cancellationToken));

    /// <summary>
    /// One tracking query as the Persona, in a span of its own. A failure, an answer it can't read and Norvia unreachable
    /// are all answers, not errors: the screen keeps what it last had and shows the failure beside it.
    /// </summary>
    private async Task<TrackingAnswer<T>> QueryTrackingAsync<T>(
        Persona persona,
        string spanName,
        string? transportOrderNumber,
        string relativeUri,
        Func<HttpResponseMessage, CancellationToken, Task<T>> read,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(persona);

        using var span = activities.Source.StartActivity(spanName, ActivityKind.Internal);
        TrackingAnswer<T> answer;
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(relativeUri, UriKind.Relative));
            request.Headers.Add("Ocp-Apim-Subscription-Key", options.Value.SubscriptionKeyOf(persona.Id));
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await tokens.GetTokenAsync(persona.Id, cancellationToken));

            using var response = await http.SendAsync(request, cancellationToken);
            var correlationId = CorrelationIdOf(response);
            if (response.IsSuccessStatusCode)
            {
                answer = TrackingAnswer.Found<T>(await read(response, cancellationToken), correlationId);
            }
            else
            {
                answer = ShipperTrackingFailures.Read<T>((int)response.StatusCode, await response.Content.ReadAsStringAsync(cancellationToken), correlationId);
            }
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            // Unreachable, too slow, no token to call with, or an answer in no format we know: the Shipper sees none.
            LogTrackingQueryFailed(exception, persona.Id.Value, spanName);
            answer = TrackingAnswer.Unavailable<T>(ShipperTrackingFailures.UnavailableText);
        }

        var keys = new Dictionary<string, object?> { ["flow"] = "shipper-tracking", ["partyId"] = persona.PartyId };
        foreach (var (key, value) in OutcomeOf(answer.Outcome))
        {
            keys[key] = value;
        }

        if (answer.MessageId is { } messageId)
        {
            keys["correlationId"] = messageId;
        }

        if (transportOrderNumber is not null)
        {
            keys["transportOrderNumber"] = transportOrderNumber;
        }

        using (logger.BeginScope(keys))
        {
            LogTrackingQueried(persona.Id.Value, spanName, answer.Outcome);
        }

        return answer;
    }

    private static Dictionary<string, object?> OutcomeOf(TrackingOutcome outcome) => outcome switch
    {
        TrackingOutcome.Found => new() { ["outcome"] = "Succeeded" },
        TrackingOutcome.NotFound or TrackingOutcome.Refused => new() { ["outcome"] = "Refused" },
        TrackingOutcome.Unavailable => new() { ["outcome"] = "Failed", ["failureCode"] = "TrackingUnavailable" },
        _ => throw new ArgumentOutOfRangeException(nameof(outcome), outcome, "Every answer records its outcome."),
    };

    private static Dictionary<string, object?> TrackingKeysOf(Persona persona, string carrierBookingNumber, string shipmentNumber, NorviaReply reply)
    {
        var keys = CarrierAnswerTracking.KeysOf(persona, carrierBookingNumber, shipmentNumber, reply.MessageId);
        foreach (var (key, value) in CarrierAnswerTracking.OutcomeOf(reply.Result))
        {
            keys[key] = value;
        }

        return keys;
    }

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

    [LoggerMessage(Level = LogLevel.Warning, Message = "A draft request by Persona {PersonaId} failed before Norvia answered ({ExceptionType})")]
    private partial void LogDraftNotAsked(string personaId, string exceptionType);

    [LoggerMessage(Level = LogLevel.Information, Message = "Norvia answered a draft request by Persona {PersonaId}: {Outcome}")]
    private partial void LogDraftAnswered(string personaId, BookingDraftOutcome outcome);

    [LoggerMessage(Level = LogLevel.Warning, Message = "A booking by Persona {PersonaId} didn't reach the Norvia Partner API")]
    private partial void LogNotSent(Exception exception, string personaId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Norvia answered a booking by Persona {PersonaId}: {Result}")]
    private partial void LogAnswered(string personaId, BookingResult result);

    [LoggerMessage(Level = LogLevel.Warning, Message = "An answer by Persona {PersonaId} to {CarrierBookingNumber} didn't reach the Norvia Partner API")]
    private partial void LogAnswerNotSent(Exception exception, string personaId, string carrierBookingNumber);

    [LoggerMessage(Level = LogLevel.Information, Message = "Norvia replied to an answer by Persona {PersonaId} to {CarrierBookingNumber}: {Result}")]
    private partial void LogAnswerReplied(string personaId, string carrierBookingNumber, CarrierAnswerResult result);

    [LoggerMessage(Level = LogLevel.Warning, Message = "A Tracking event by Persona {PersonaId} for {CarrierBookingNumber} didn't reach the Norvia Partner API")]
    private partial void LogTrackingNotSent(Exception exception, string personaId, string carrierBookingNumber);

    [LoggerMessage(Level = LogLevel.Information, Message = "The Norvia Partner API replied to a Tracking event by Persona {PersonaId} for {CarrierBookingNumber}: {Result}")]
    private partial void LogTrackingReplied(string personaId, string carrierBookingNumber, TrackingResult result);

    [LoggerMessage(Level = LogLevel.Warning, Message = "A Proof of delivery by Persona {PersonaId} for {CarrierBookingNumber} didn't reach the Norvia Partner API")]
    private partial void LogProofOfDeliveryNotSent(Exception exception, string personaId, string carrierBookingNumber);

    [LoggerMessage(Level = LogLevel.Information, Message = "The Norvia Partner API replied to a Proof of delivery by Persona {PersonaId} for {CarrierBookingNumber}: {Result}")]
    private partial void LogProofOfDeliveryReplied(string personaId, string carrierBookingNumber, TrackingResult result);

    [LoggerMessage(Level = LogLevel.Warning, Message = "A Carrier invoice {InvoiceNumber} by Persona {PersonaId} didn't reach the Norvia Partner API")]
    private partial void LogCarrierInvoiceNotSent(Exception exception, string personaId, string invoiceNumber);

    [LoggerMessage(Level = LogLevel.Information, Message = "The Norvia Partner API replied to the Carrier invoice {InvoiceNumber} by Persona {PersonaId}: {Result}")]
    private partial void LogCarrierInvoiceReplied(string personaId, string invoiceNumber, CarrierInvoiceReplyResult result);

    [LoggerMessage(Level = LogLevel.Warning, Message = "A tracking query ({Query}) by Persona {PersonaId} got no answer it could read")]
    private partial void LogTrackingQueryFailed(Exception exception, string personaId, string query);

    [LoggerMessage(Level = LogLevel.Information, Message = "Norvia answered the tracking query ({Query}) by Persona {PersonaId}: {Outcome}")]
    private partial void LogTrackingQueried(string personaId, string query, TrackingOutcome outcome);
}
