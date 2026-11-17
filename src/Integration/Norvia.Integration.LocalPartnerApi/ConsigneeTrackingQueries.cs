using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

using Norvia.Integration.LocalSupport;

namespace Norvia.Integration.LocalPartnerApi;

/// <summary>
/// The Norvia Partner API's one Consignee operation, "Get a Consignee's view by tracking code", played locally (ADR 0052, ADR 0023). Like
/// infra/estate/partner-api/get-consignee-view.policy.xml it checks the Consignee tracking product's subscription key and nothing else:
/// no token and no Persona, as the code in the <c>X-Tracking-Code</c> header is the credential. It limits each client IP, queries the TMS
/// as API Management's identity, answers a wrong, expired or replaced code with one fixed answer, and maps the rest with the policy's Liquid
/// template. The code is never logged: the logs carry the Transport Order number it resolved to, or <c>none</c>.
/// </summary>
internal static partial class ConsigneeTrackingQueries
{
    public const string Route = "/consignee/v1/view";

    private const string NotFoundBody = "{\"code\":\"TrackingCodeNotFound\",\"text\":\"We can't find this shipment.\"}";

    public static IEndpointRouteBuilder MapConsigneeTrackingQueries(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet(Route, HandleAsync).AllowAnonymous();
        return endpoints;
    }

    private static async Task<IResult> HandleAsync(
        HttpContext context,
        Microsoft.Extensions.Options.IOptions<PartnerApiOptions> options,
        TmsShipperTracking tms,
        ConsigneeAbuseLimits limits,
        ShipperTrackingMap map,
        ILoggerFactory loggers)
    {
        var correlationId = Guid.NewGuid().ToString();
        context.Response.Headers["X-Correlation-ID"] = correlationId;
        context.Response.Headers.CacheControl = "no-store";
        var logger = loggers.CreateLogger(typeof(ConsigneeTrackingQueries));
        using var trail = logger.BeginScope(new Dictionary<string, object?> { [TrackingKeys.CorrelationId] = correlationId, [TrackingKeys.Flow] = TrackingKeys.ConsigneeTracking });

        var key = context.Request.Headers["Ocp-Apim-Subscription-Key"].ToString();
        if (string.IsNullOrEmpty(key) || options.Value.ConsigneeSubscriptionKey is not { Length: > 0 } expected || key != expected)
        {
            using var refused = logger.BeginScope(TrackingKeys.OutcomeOf(TrackingKeys.Refused, "InvalidSubscriptionKey"));
            LogRefused(logger, StatusCodes.Status401Unauthorized, "InvalidSubscriptionKey");
            return Json(StatusCodes.Status401Unauthorized, new JsonObject
            {
                ["statusCode"] = StatusCodes.Status401Unauthorized,
                ["message"] = "Access denied due to invalid subscription key. Make sure to provide a valid key for an active subscription.",
            });
        }

        // The portal is the caller, so the Consignee's own address comes in X-Client-IP, as the policy reads it.
        var clientIp = context.Request.Headers["X-Client-IP"].ToString() is { Length: > 0 } forwarded ? forwarded : context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        if (limits.CountCall(clientIp) is { } retryAfter)
        {
            return RateLimited(context, logger, (int)Math.Ceiling(retryAfter.TotalSeconds));
        }

        if (limits.TooManyMisses(clientIp))
        {
            return RateLimited(context, logger, (int)ConsigneeAbuseLimits.Window.TotalSeconds);
        }

        var trackingCode = context.Request.Headers["X-Tracking-Code"].ToString();
        var answer = string.IsNullOrWhiteSpace(trackingCode)
            ? new TmsAnswer(StatusCodes.Status404NotFound, [], null, null)
            : await tms.QueryConsigneeAsync(trackingCode, correlationId, context.RequestAborted);
        if (answer.StatusCode is 0 or >= 500)
        {
            return Unavailable(logger, correlationId, "TmsUnavailable", "none");
        }

        if (answer.StatusCode != StatusCodes.Status200OK)
        {
            // A wrong, expired and replaced code, and one that is malformed, are one answer.
            limits.CountMiss(clientIp);
            using var missed = logger.BeginScope(WithOrder(TrackingKeys.OutcomeOf(TrackingKeys.Refused, "TrackingCodeNotFound"), "none"));
            LogMissed(logger);
            return TypedResults.Content(NotFoundBody, "application/json", Encoding.UTF8, StatusCodes.Status404NotFound);
        }

        var canonical = Encoding.UTF8.GetString(answer.Content);
        var transportOrderNumber = JsonNode.Parse(canonical)?["transportOrderNumber"]?.GetValue<string>() ?? "none";
        var mapped = map.Consignee(canonical);
        if (EmptyCode().IsMatch(mapped))
        {
            return Unavailable(logger, correlationId, "TrackingNotMapped", transportOrderNumber);
        }

        using var succeeded = logger.BeginScope(WithOrder(TrackingKeys.OutcomeOf(TrackingKeys.Succeeded), transportOrderNumber));
        LogAnswered(logger, transportOrderNumber);
        return TypedResults.Content(mapped, "application/json", Encoding.UTF8, StatusCodes.Status200OK);
    }

    // The Transport Order the code resolved to, or none: the one business key a Consignee's trail may carry.
    private static Dictionary<string, object?> WithOrder(Dictionary<string, object?> keys, string transportOrderNumber)
    {
        keys[TrackingKeys.TransportOrderNumber] = transportOrderNumber;
        return keys;
    }

    private static Microsoft.AspNetCore.Http.HttpResults.ContentHttpResult RateLimited(HttpContext context, ILogger logger, int seconds)
    {
        using var limited = logger.BeginScope(WithOrder(TrackingKeys.OutcomeOf(TrackingKeys.Refused, "RateLimited"), "none"));
        LogRefused(logger, StatusCodes.Status429TooManyRequests, "RateLimited");
        context.Response.Headers.RetryAfter = seconds.ToString(System.Globalization.CultureInfo.InvariantCulture);
        return Json(StatusCodes.Status429TooManyRequests, new JsonObject { ["code"] = "RateLimited", ["text"] = "Too many tries. Try again in a minute." });
    }

    private static Microsoft.AspNetCore.Http.HttpResults.ContentHttpResult Unavailable(ILogger logger, string correlationId, string failureCode, string transportOrderNumber)
    {
        using var failed = logger.BeginScope(WithOrder(TrackingKeys.OutcomeOf(TrackingKeys.Failed, failureCode), transportOrderNumber));
        LogUnavailable(logger, failureCode);
        return Json(StatusCodes.Status503ServiceUnavailable, new JsonObject
        {
            ["messageId"] = correlationId,
            ["text"] = "Tracking is temporarily unavailable. Try again shortly.",
        });
    }

    private static Microsoft.AspNetCore.Http.HttpResults.ContentHttpResult Json(int statusCode, JsonObject body) =>
        TypedResults.Content(body.ToJsonString(), "application/json", statusCode: statusCode);

    [GeneratedRegex("\"(status|code)\"\\s*:\\s*\"\"")]
    private static partial Regex EmptyCode();

    [LoggerMessage(Level = LogLevel.Information, Message = "Refused at the edge with {StatusCode} ({FailureCode})")]
    private static partial void LogRefused(ILogger logger, int statusCode, string failureCode);

    [LoggerMessage(Level = LogLevel.Information, Message = "A tracking code matched no Transport Order: none")]
    private static partial void LogMissed(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Answered the Consignee for Transport Order {TransportOrderNumber}")]
    private static partial void LogAnswered(ILogger logger, string transportOrderNumber);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Tracking is temporarily unavailable ({FailureCode})")]
    private static partial void LogUnavailable(ILogger logger, string failureCode);
}
