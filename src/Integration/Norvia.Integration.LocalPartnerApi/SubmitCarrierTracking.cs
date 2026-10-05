using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Norvia.Integration.LocalPartnerApi;

/// <summary>
/// The Norvia Partner API's "submit Carrier tracking", played locally: API Management's checks at the edge, then the
/// message on the tracking queue and 202 Accepted (ADR 0008, ADR 0023). Like the policy it calls no Logic App and never
/// the TMS: the Carrier-tracking Logic App's work is done later, from the queue.
/// </summary>
internal static partial class SubmitCarrierTracking
{
    public static IEndpointRouteBuilder MapSubmitCarrierTracking(this IEndpointRouteBuilder endpoints)
    {
        // Authorization is the edge's own checks, in API Management's order, not the ASP.NET Core pipeline's.
        endpoints.MapPost("/partner/v1/carrier-tracking", HandleAsync).AllowAnonymous();
        return endpoints;
    }

    public static async Task<ContentHttpResult> HandleAsync(HttpContext context, PartnerEdge edge, ITrackingQueue queue, ILoggerFactory loggers)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(edge);
        ArgumentNullException.ThrowIfNull(queue);
        ArgumentNullException.ThrowIfNull(loggers);

        // A new one for each message, whatever the caller sent, as API Management creates it.
        var correlationId = Guid.NewGuid().ToString();
        context.Response.Headers["X-Correlation-ID"] = correlationId;
        var logger = loggers.CreateLogger(typeof(SubmitCarrierTracking));
        using var trail = logger.BeginScope(new Dictionary<string, object?> { [TrackingKeys.CorrelationId] = correlationId, [TrackingKeys.Flow] = TrackingKeys.CarrierTracking });

        var check = await edge.CheckAsync(context.Request, EdgeRule.SubmitCarrierTracking, context.RequestAborted);
        if (check.PartyId is not { } carrierId)
        {
            using var refused = logger.BeginScope(TrackingKeys.OutcomeOf(TrackingKeys.Refused, check.FailureCode));
            LogRefused(logger, check.RefusalStatus, check.FailureCode);
            return check.RefusalStatus == StatusCodes.Status401Unauthorized
                ? Answer(check.RefusalStatus, new JsonObject { ["statusCode"] = check.RefusalStatus, ["message"] = check.RefusalText })
                : Answer(check.RefusalStatus, new JsonObject { ["messageId"] = correlationId, ["text"] = check.RefusalText });
        }

        using var reader = new StreamReader(context.Request.Body);
        var body = await reader.ReadToEndAsync(context.RequestAborted);

        // The tracking contract's keys (coding guidelines §7) go on the message as application properties, so a message
        // Service Bus dead-letters itself can still be found. The source message ID is the Carrier's event ID, when it has one.
        var properties = new Dictionary<string, string>
        {
            [TrackingKeys.CorrelationId] = correlationId,
            [TrackingKeys.PartyId] = carrierId,
            [TrackingKeys.Flow] = TrackingKeys.CarrierTracking,
        };
        if (EventIdOf(body) is { } eventId)
        {
            properties[TrackingKeys.SourceMessageId] = eventId;
        }

        var keys = properties.Where(property => property.Key is not (TrackingKeys.CorrelationId or TrackingKeys.Flow))
            .ToDictionary(property => property.Key, property => (object?)property.Value);
        using var party = logger.BeginScope(keys);
        try
        {
            await queue.EnqueueAsync(new TrackingMessage(body, correlationId, properties), context.RequestAborted);
        }
        catch (Exception exception) when (!context.RequestAborted.IsCancellationRequested)
        {
            using var failed = logger.BeginScope(TrackingKeys.OutcomeOf(TrackingKeys.Failed, "TrackingNotQueued"));
            LogNotQueued(logger, exception);
            return Answer(StatusCodes.Status503ServiceUnavailable, new JsonObject
            {
                ["messageId"] = correlationId,
                ["text"] = "The tracking message was not accepted. Send it again.",
            });
        }

        using var accepted = logger.BeginScope(TrackingKeys.OutcomeOf(TrackingKeys.Succeeded));
        LogAccepted(logger);
        return Answer(StatusCodes.Status202Accepted, new JsonObject { ["messageId"] = correlationId });
    }

    // The Carrier's own event ID, when the body is a JSON object that has one as text.
    private static string? EventIdOf(string body)
    {
        try
        {
            return JsonNode.Parse(body) is JsonObject message && message["eventId"] is JsonValue value && value.TryGetValue<string>(out var eventId) ? eventId : null;
        }
        catch (System.Text.Json.JsonException)
        {
            return null;
        }
    }

    private static ContentHttpResult Answer(int statusCode, JsonObject body) =>
        TypedResults.Content(body.ToJsonString(), "application/json", statusCode: statusCode);

    [LoggerMessage(Level = LogLevel.Information, Message = "Refused at the edge with {StatusCode} ({FailureCode})")]
    private static partial void LogRefused(ILogger logger, int statusCode, string? failureCode);

    [LoggerMessage(Level = LogLevel.Information, Message = "Accepted: put on the tracking queue")]
    private static partial void LogAccepted(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The tracking message could not be put on the tracking queue")]
    private static partial void LogNotQueued(ILogger logger, Exception exception);
}
