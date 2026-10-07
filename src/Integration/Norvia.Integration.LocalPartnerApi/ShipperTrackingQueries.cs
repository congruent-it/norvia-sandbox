using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

using Norvia.Integration.LocalSupport;

namespace Norvia.Integration.LocalPartnerApi;

/// <summary>
/// The Norvia Partner API's four Shipper tracking operations (list, get, find and a Proof of delivery file), played
/// locally (P8, ADR 0023). Like the policies in infra/estate/partner-api/ it checks, in this order: the subscription key,
/// a token for the Norvia Partner API, the <c>Tracking.Read</c> role, a Shipper for the token's <c>azp</c> and the rate
/// limit per subscription. It creates the correlation ID, queries the TMS as API Management's identity with the Shipper ID
/// and the correlation ID, waits a short time and doesn't retry, and maps the answer with the policies' Liquid templates. A
/// file and a refusal pass through unchanged. No Logic App is involved, as none is on a read path (ADR 0003).
/// </summary>
internal static partial class ShipperTrackingQueries
{
    private const string TmsRoute = "/v1/shipper-tracking/transport-orders";

    private enum Answer
    {
        List,
        Detail,
        File,
    }

    public static IEndpointRouteBuilder MapShipperTrackingQueries(this IEndpointRouteBuilder endpoints)
    {
        // Authorization is the edge's own checks, in API Management's order, not the ASP.NET Core pipeline's.
        endpoints.MapGet("/partner/v1/transport-orders", (HttpContext context, Services services) =>
            HandleAsync(context, services, TmsRoute, Answer.List)).AllowAnonymous();
        endpoints.MapGet("/partner/v1/transport-orders/find", (HttpContext context, Services services) =>
            HandleAsync(context, services, $"{TmsRoute}/find", Answer.Detail)).AllowAnonymous();
        endpoints.MapGet("/partner/v1/transport-orders/{transportOrderNumber}", (HttpContext context, Services services, string transportOrderNumber) =>
            HandleAsync(context, services, $"{TmsRoute}/{Uri.EscapeDataString(transportOrderNumber)}", Answer.Detail)).AllowAnonymous();
        endpoints.MapGet("/partner/v1/transport-orders/{transportOrderNumber}/proof-of-delivery/files/{fileId}", (HttpContext context, Services services, string transportOrderNumber, string fileId) =>
            HandleAsync(context, services, $"{TmsRoute}/{Uri.EscapeDataString(transportOrderNumber)}/proof-of-delivery/files/{Uri.EscapeDataString(fileId)}", Answer.File)).AllowAnonymous();
        return endpoints;
    }

    // What the four operations share, bound together so each route's lambda stays short.
    internal sealed record Services(PartnerEdge Edge, TmsShipperTracking Tms, TrackingRateLimit RateLimit, ShipperTrackingMap Map, ILoggerFactory Loggers)
    {
        public static ValueTask<Services?> BindAsync(HttpContext context)
        {
            var provider = context.RequestServices;
            return ValueTask.FromResult<Services?>(new Services(
                provider.GetRequiredService<PartnerEdge>(),
                provider.GetRequiredService<TmsShipperTracking>(),
                provider.GetRequiredService<TrackingRateLimit>(),
                provider.GetRequiredService<ShipperTrackingMap>(),
                provider.GetRequiredService<ILoggerFactory>()));
        }
    }

    private static async Task<IResult> HandleAsync(HttpContext context, Services services, string tmsRoute, Answer answer)
    {
        // A new one for each query, whatever the caller sent, as API Management creates it.
        var correlationId = Guid.NewGuid().ToString();
        context.Response.Headers["X-Correlation-ID"] = correlationId;
        var logger = services.Loggers.CreateLogger(typeof(ShipperTrackingQueries));
        using var trail = logger.BeginScope(new Dictionary<string, object?> { [TrackingKeys.CorrelationId] = correlationId, [TrackingKeys.Flow] = TrackingKeys.ShipperTracking });

        var check = await services.Edge.CheckAsync(context.Request, EdgeRule.ReadShipperTracking, context.RequestAborted);
        if (check.PartyId is not { } shipperId)
        {
            using var refused = logger.BeginScope(TrackingKeys.OutcomeOf(TrackingKeys.Refused, check.FailureCode));
            LogRefused(logger, check.RefusalStatus, check.FailureCode);
            return check.RefusalStatus == StatusCodes.Status401Unauthorized
                ? Json(check.RefusalStatus, new JsonObject { ["statusCode"] = check.RefusalStatus, ["message"] = check.RefusalText })
                : Json(check.RefusalStatus, new JsonObject { ["messageId"] = correlationId, ["text"] = check.RefusalText });
        }

        using var party = logger.BeginScope(new Dictionary<string, object?> { [TrackingKeys.PartyId] = shipperId });

        // One counter per subscription, after the checks, so a refusal is not counted.
        if (services.RateLimit.Count(context.Request.Headers["Ocp-Apim-Subscription-Key"].ToString()) is { } retryAfter)
        {
            using var limited = logger.BeginScope(TrackingKeys.OutcomeOf(TrackingKeys.Refused, "RateLimitExceeded"));
            LogRefused(logger, StatusCodes.Status429TooManyRequests, "RateLimitExceeded");
            var seconds = (int)Math.Ceiling(retryAfter.TotalSeconds);
            context.Response.Headers.RetryAfter = seconds.ToString(System.Globalization.CultureInfo.InvariantCulture);
            return Json(StatusCodes.Status429TooManyRequests, new JsonObject
            {
                ["statusCode"] = StatusCodes.Status429TooManyRequests,
                ["message"] = $"Rate limit is exceeded. Try again in {seconds} seconds.",
            });
        }

        var tms = await services.Tms.QueryAsync(tmsRoute + context.Request.QueryString.Value, shipperId, correlationId, context.RequestAborted);
        if (tms.StatusCode is 0 or >= 500)
        {
            return Unavailable(logger, correlationId, "TmsUnavailable");
        }

        if (tms.StatusCode != StatusCodes.Status200OK)
        {
            using var tmsRefused = logger.BeginScope(TrackingKeys.OutcomeOf(TrackingKeys.Refused, "TmsRefused"));
            LogAnswered(logger, tms.StatusCode);
            return new PassThrough(tms);
        }

        if (answer is Answer.File)
        {
            using var sent = logger.BeginScope(TrackingKeys.OutcomeOf(TrackingKeys.Succeeded));
            LogAnswered(logger, tms.StatusCode);
            return new PassThrough(tms);
        }

        var canonical = Encoding.UTF8.GetString(tms.Content);
        var mapped = answer is Answer.List ? services.Map.List(canonical) : services.Map.Detail(canonical);

        // The map gives an empty code for a Tracking status or event it has no code for (ADR 0035), and the policy refuses that.
        if (EmptyCode().IsMatch(mapped))
        {
            return Unavailable(logger, correlationId, "TrackingNotMapped");
        }

        using var mappedOutcome = logger.BeginScope(TrackingKeys.OutcomeOf(TrackingKeys.Succeeded));
        LogAnswered(logger, tms.StatusCode);
        return TypedResults.Content(mapped, "application/json", Encoding.UTF8, StatusCodes.Status200OK);
    }

    private static Microsoft.AspNetCore.Http.HttpResults.ContentHttpResult Unavailable(ILogger logger, string correlationId, string failureCode)
    {
        using var failed = logger.BeginScope(TrackingKeys.OutcomeOf(TrackingKeys.Failed, failureCode));
        LogUnavailable(logger, failureCode);
        return Json(StatusCodes.Status503ServiceUnavailable, new JsonObject
        {
            ["messageId"] = correlationId,
            ["text"] = "Tracking is temporarily unavailable. Try again shortly.",
        });
    }

    private static Microsoft.AspNetCore.Http.HttpResults.ContentHttpResult Json(int statusCode, JsonObject body) =>
        TypedResults.Content(body.ToJsonString(), "application/json", statusCode: statusCode);

    // As the policy's regular expression: "status" or "code" with nothing in it.
    [GeneratedRegex("\"(status|code)\"\\s*:\\s*\"\"")]
    private static partial Regex EmptyCode();

    /// <summary>The TMS's answer as it came, for a file and for a refusal: the status, the content type, the file's name and the bytes.</summary>
    private sealed class PassThrough(TmsAnswer answer) : IResult
    {
        public async Task ExecuteAsync(HttpContext httpContext)
        {
            httpContext.Response.StatusCode = answer.StatusCode;
            if (answer.ContentType is not null)
            {
                httpContext.Response.ContentType = answer.ContentType;
            }

            if (answer.ContentDisposition is not null)
            {
                httpContext.Response.Headers.ContentDisposition = answer.ContentDisposition;
            }

            await httpContext.Response.Body.WriteAsync(answer.Content, httpContext.RequestAborted);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Refused at the edge with {StatusCode} ({FailureCode})")]
    private static partial void LogRefused(ILogger logger, int statusCode, string? failureCode);

    [LoggerMessage(Level = LogLevel.Information, Message = "Answered the Shipper with what the TMS answered ({StatusCode})")]
    private static partial void LogAnswered(ILogger logger, int statusCode);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Tracking is temporarily unavailable ({FailureCode})")]
    private static partial void LogUnavailable(ILogger logger, string failureCode);
}
