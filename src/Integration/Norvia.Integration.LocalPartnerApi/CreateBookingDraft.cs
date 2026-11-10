using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Options;
using Norvia.Integration.BookingDraft;
using Norvia.Integration.LocalSupport;

namespace Norvia.Integration.LocalPartnerApi;

/// <summary>
/// The Norvia Partner API's "create Booking draft", played locally (ADR 0023, ADR 0040): API Management's checks at the edge in the policies'
/// order (subscription key, token, <c>TransportOrders.Draft</c>, a Shipper for the <c>azp</c>, the rate limit), then the Intake agent. The
/// draft is stateless and stored nowhere. Neither the request's text nor the draft is ever logged: the trail holds keys only.
/// </summary>
internal static partial class CreateBookingDraft
{
    public const string AgentsDisabled = BookingDraftOperation.AgentsDisabled;
    public const string InputTooLarge = BookingDraftOperation.InputTooLarge;
    public const string TextRequired = BookingDraftOperation.TextRequired;

    public static IEndpointRouteBuilder MapCreateBookingDraft(this IEndpointRouteBuilder endpoints)
    {
        // Authorization is the edge's own checks, in API Management's order, not the ASP.NET Core pipeline's.
        endpoints.MapPost("/partner/v1/booking-drafts", HandleAsync).AllowAnonymous();
        return endpoints;
    }

    public static async Task<ContentHttpResult> HandleAsync(
        HttpContext context,
        PartnerEdge edge,
        DraftRateLimit rateLimit,
        BookingDraftExtractor extractor,
        IOptions<AgentsOptions> options,
        ILoggerFactory loggers)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(edge);
        ArgumentNullException.ThrowIfNull(rateLimit);
        ArgumentNullException.ThrowIfNull(extractor);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(loggers);

        // Created at the edge, whatever the caller sent, as API Management does.
        var correlationId = Guid.NewGuid().ToString();
        context.Response.Headers["X-Correlation-ID"] = correlationId;
        var logger = loggers.CreateLogger(typeof(CreateBookingDraft));
        using var trail = logger.BeginScope(new Dictionary<string, object?> { [TrackingKeys.CorrelationId] = correlationId, [TrackingKeys.Flow] = TrackingKeys.BookingDraft });

        var check = await edge.CheckAsync(context.Request, EdgeRule.DraftBooking, context.RequestAborted);
        if (check.PartyId is not { } shipperId)
        {
            using var refused = logger.BeginScope(TrackingKeys.OutcomeOf(TrackingKeys.Refused, check.FailureCode));
            LogRefused(logger, check.RefusalStatus, check.FailureCode);
            return check.RefusalStatus == StatusCodes.Status401Unauthorized
                ? Answer(check.RefusalStatus, new JsonObject { ["statusCode"] = check.RefusalStatus, ["message"] = check.RefusalText })
                : Answer(check.RefusalStatus, new JsonObject { ["messageId"] = correlationId, ["text"] = check.RefusalText });
        }

        using var party = logger.BeginScope(new Dictionary<string, object?> { [TrackingKeys.PartyId] = shipperId });

        // A switched-off assistant uses no one's rate limit.
        var agents = options.Value;
        if (!agents.Enabled)
        {
            return Refuse(logger, BookingDraftOperation.Refuse(correlationId, StatusCodes.Status503ServiceUnavailable, AgentsDisabled, "The assistant is unavailable."));
        }

        // One counter per subscription, after the checks, so a refusal is not counted.
        if (rateLimit.Count(context.Request.Headers["Ocp-Apim-Subscription-Key"].ToString()) is { } retryAfter)
        {
            var seconds = (int)Math.Ceiling(retryAfter.TotalSeconds);
            context.Response.Headers.RetryAfter = seconds.ToString(System.Globalization.CultureInfo.InvariantCulture);
            return Refuse(logger, BookingDraftOperation.Refuse(correlationId, StatusCodes.Status429TooManyRequests, "RateLimitExceeded", $"Rate limit is exceeded. Try again in {seconds} seconds."));
        }

        var answer = await BookingDraftOperation.DraftAsync(extractor, context.Request.Body, context.Request.ContentType, agents.Limits, correlationId, context.RequestAborted);
        return answer.Outcome switch
        {
            BookingDraftOperation.Succeeded => Succeed(logger, answer),
            BookingDraftOperation.Refused => Refuse(logger, answer),
            _ => Fail(logger, answer),
        };
    }

    private static ContentHttpResult Succeed(ILogger logger, BookingDraftAnswer answer)
    {
        using var succeeded = logger.BeginScope(TrackingKeys.OutcomeOf(TrackingKeys.Succeeded));
        LogDrafted(logger);
        return Answer(answer);
    }

    private static ContentHttpResult Refuse(ILogger logger, BookingDraftAnswer answer)
    {
        using var refused = logger.BeginScope(TrackingKeys.OutcomeOf(TrackingKeys.Refused, answer.FailureCode));
        LogRefused(logger, answer.StatusCode, answer.FailureCode);
        return Answer(answer);
    }

    private static ContentHttpResult Fail(ILogger logger, BookingDraftAnswer answer)
    {
        using var failed = logger.BeginScope(TrackingKeys.OutcomeOf(TrackingKeys.Failed, answer.FailureCode));
        LogFailed(logger, answer.FailureCode);
        return Answer(answer);
    }

    private static ContentHttpResult Answer(BookingDraftAnswer answer) => Answer(answer.StatusCode, answer.Body);

    private static ContentHttpResult Answer(int statusCode, JsonObject body) =>
        TypedResults.Content(body.ToJsonString(), "application/json", statusCode: statusCode);

    [LoggerMessage(Level = LogLevel.Information, Message = "Refused with {StatusCode} ({FailureCode})")]
    private static partial void LogRefused(ILogger logger, int statusCode, string? failureCode);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The assistant failed ({FailureCode})")]
    private static partial void LogFailed(ILogger logger, string? failureCode);

    [LoggerMessage(Level = LogLevel.Information, Message = "Answered the Shipper with a Booking draft")]
    private static partial void LogDrafted(ILogger logger);
}
