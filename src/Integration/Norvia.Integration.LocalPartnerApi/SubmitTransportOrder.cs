using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Http.HttpResults;
using Norvia.Integration.TransportOrderIntake;

namespace Norvia.Integration.LocalPartnerApi;

/// <summary>
/// The Norvia Partner API's "submit Transport Order", played locally: API Management's checks at the edge, then the
/// intake Logic App's map, TMS call and relay, with the Logic App's own code (ADR 0023).
/// </summary>
internal static partial class SubmitTransportOrder
{
    public static IEndpointRouteBuilder MapSubmitTransportOrder(this IEndpointRouteBuilder endpoints)
    {
        // Authorization is the edge's own checks, in API Management's order, not the ASP.NET Core pipeline's.
        endpoints.MapPost("/partner/v1/transport-orders", HandleAsync).AllowAnonymous();
        return endpoints;
    }

    public static async Task<ContentHttpResult> HandleAsync(HttpContext context, PartnerEdge edge, TmsIntake tms, ILoggerFactory loggers)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(edge);
        ArgumentNullException.ThrowIfNull(tms);
        ArgumentNullException.ThrowIfNull(loggers);

        // Created at the edge, whatever the caller sent, as API Management does.
        var correlationId = Guid.NewGuid().ToString();
        context.Response.Headers["X-Correlation-ID"] = correlationId;
        var logger = loggers.CreateLogger(typeof(SubmitTransportOrder));
        using var trail = logger.BeginScope(new Dictionary<string, object?> { [TrackingKeys.CorrelationId] = correlationId, [TrackingKeys.Flow] = TrackingKeys.TransportOrderIntake });

        var check = await edge.CheckAsync(context.Request, context.RequestAborted);
        if (check.ShipperId is not { } shipperId)
        {
            // As the policy's trace metadata records a refusal.
            using var refused = logger.BeginScope(TrackingKeys.OutcomeOf(TrackingKeys.Refused, check.FailureCode));
            LogRefused(logger, check.RefusalStatus, check.FailureCode);
            return check.RefusalStatus == StatusCodes.Status401Unauthorized
                ? Answer(check.RefusalStatus, new JsonObject { ["statusCode"] = check.RefusalStatus, ["message"] = check.RefusalText })
                : Answer(check.RefusalStatus, new JsonObject { ["messageId"] = correlationId, ["text"] = check.RefusalText });
        }

        using var party = logger.BeginScope(new Dictionary<string, object?> { [TrackingKeys.PartyId] = shipperId });
        using var reader = new StreamReader(context.Request.Body);
        var booking = await reader.ReadToEndAsync(context.RequestAborted);

        var transportOrder = IntakeMap.ToTransportOrder(booking, shipperId);
        var (statusCode, body) = await tms.SubmitAsync(transportOrder.ToJsonString(), correlationId, context.RequestAborted);
        var answer = BookingResponses.Relay(statusCode, body, correlationId, booking);

        using var tmsCall = logger.BeginScope(TmsCallTracking.Of(statusCode, body));
        LogAnswered(logger, answer.StatusCode, answer.FailureCode);
        return Answer(answer.StatusCode, answer.Body);
    }

    private static ContentHttpResult Answer(int statusCode, JsonObject body) =>
        TypedResults.Content(body.ToJsonString(), "application/json", statusCode: statusCode);

    [LoggerMessage(Level = LogLevel.Information, Message = "Refused at the edge with {StatusCode} ({FailureCode})")]
    private static partial void LogRefused(ILogger logger, int statusCode, string? failureCode);

    [LoggerMessage(Level = LogLevel.Information, Message = "Answered the partner with {StatusCode} (failure code {FailureCode})")]
    private static partial void LogAnswered(ILogger logger, int statusCode, string? failureCode);
}
