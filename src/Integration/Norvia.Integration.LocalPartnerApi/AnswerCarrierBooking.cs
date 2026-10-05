using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Http.HttpResults;
using Norvia.Integration.CarrierBooking;
using Norvia.Integration.TransportOrderIntake;

using Norvia.Integration.LocalSupport;

namespace Norvia.Integration.LocalPartnerApi;

/// <summary>
/// The Norvia Partner API's "answer Carrier booking", played locally: API Management's checks at the edge, then the
/// Carrier-answer Logic App's map, TMS call and relay, with the Logic App's own code (ADR 0023).
/// </summary>
internal static partial class AnswerCarrierBooking
{
    public static IEndpointRouteBuilder MapAnswerCarrierBooking(this IEndpointRouteBuilder endpoints)
    {
        // Authorization is the edge's own checks, in API Management's order, not the ASP.NET Core pipeline's.
        endpoints.MapPost("/partner/v1/carrier-answers", HandleAsync).AllowAnonymous();
        return endpoints;
    }

    public static async Task<ContentHttpResult> HandleAsync(HttpContext context, PartnerEdge edge, TmsCarrierAnswers tms, ILoggerFactory loggers)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(edge);
        ArgumentNullException.ThrowIfNull(tms);
        ArgumentNullException.ThrowIfNull(loggers);

        // A new one for each answer, whatever the caller sent, as API Management creates it.
        var correlationId = Guid.NewGuid().ToString();
        context.Response.Headers["X-Correlation-ID"] = correlationId;
        var logger = loggers.CreateLogger(typeof(AnswerCarrierBooking));
        using var trail = logger.BeginScope(new Dictionary<string, object?> { [TrackingKeys.CorrelationId] = correlationId, [TrackingKeys.Flow] = TrackingKeys.CarrierAnswer });

        var check = await edge.CheckAsync(context.Request, EdgeRule.AnswerCarrierBooking, context.RequestAborted);
        if (check.PartyId is not { } carrierId)
        {
            // As the policy's trace metadata records a refusal.
            using var refused = logger.BeginScope(TrackingKeys.OutcomeOf(TrackingKeys.Refused, check.FailureCode));
            LogRefused(logger, check.RefusalStatus, check.FailureCode);
            return check.RefusalStatus == StatusCodes.Status401Unauthorized
                ? Answer(check.RefusalStatus, new JsonObject { ["statusCode"] = check.RefusalStatus, ["message"] = check.RefusalText })
                : Answer(check.RefusalStatus, new JsonObject { ["messageId"] = correlationId, ["text"] = check.RefusalText });
        }

        using var party = logger.BeginScope(new Dictionary<string, object?> { [TrackingKeys.PartyId] = carrierId });
        using var reader = new StreamReader(context.Request.Body);
        var carrierAnswer = await reader.ReadToEndAsync(context.RequestAborted);

        // The Carrier is the one of the token: nothing the payload names reaches the TMS (ADR 0005).
        var mapped = CarrierAnswerMap.ToCarrierBookingAnswer(carrierAnswer, carrierId);
        var (statusCode, body) = mapped.TmsRequest is { } request
            ? await tms.AnswerAsync(mapped.CarrierBookingNumber!, request.ToJsonString(), correlationId, context.RequestAborted)
            : (0, string.Empty);
        var answer = CarrierAnswerResponses.Relay(mapped, statusCode, body, correlationId);

        using var tmsCall = logger.BeginScope(CarrierAnswerTracking.Of(mapped, statusCode, body));
        LogAnswered(logger, answer.StatusCode, answer.FailureCode);
        return Answer(answer.StatusCode, answer.Body);
    }

    private static ContentHttpResult Answer(int statusCode, JsonObject body) =>
        TypedResults.Content(body.ToJsonString(), "application/json", statusCode: statusCode);

    [LoggerMessage(Level = LogLevel.Information, Message = "Refused at the edge with {StatusCode} ({FailureCode})")]
    private static partial void LogRefused(ILogger logger, int statusCode, string? failureCode);

    [LoggerMessage(Level = LogLevel.Information, Message = "Answered the Carrier with {StatusCode} (failure code {FailureCode})")]
    private static partial void LogAnswered(ILogger logger, int statusCode, string? failureCode);
}
