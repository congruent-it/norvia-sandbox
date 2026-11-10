using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Options;

namespace Norvia.Integration.BookingDraft.Api;

/// <summary>
/// The Booking draft operation behind API Management (ADR 0040). The policy has already checked the subscription key, the Shipper's token and its
/// <c>TransportOrders.Draft</c> role, taken the Shipper from the token's <c>azp</c>, applied the rate limit and the size cap and made the correlation ID;
/// they arrive as headers that only the gateway's own token may send. The host decides nothing about the Shipper. It runs the Intake agent and
/// answers as the stand-in does. The text and the draft are never logged: the trail holds keys only.
/// </summary>
internal static partial class BookingDraftEndpoint
{
    public const string Route = "/draft/v1/booking-drafts";
    public const string CorrelationHeader = "X-Correlation-ID";
    public const string ShipperHeader = "X-Shipper-ID";

    /// <summary>The <c>flow</c> of a Shipper's Booking draft request, the same as the policy's and the stand-in's.</summary>
    public const string Flow = "booking-draft";

    public static IEndpointRouteBuilder MapBookingDraft(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        endpoints.MapPost(Route, HandleAsync).RequireAuthorization(GatewayAuthentication.Policy);
        return endpoints;
    }

    public static async Task<ContentHttpResult> HandleAsync(
        HttpContext context,
        BookingDraftExtractor extractor,
        IOptions<BookingDraftHostOptions> options,
        ILoggerFactory loggers)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(extractor);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(loggers);

        // The gateway made the correlation ID. A call without one is not the gateway's, and starts a trail of its own.
        var correlationId = context.Request.Headers[CorrelationHeader].ToString() is { Length: > 0 } given ? given : Guid.NewGuid().ToString();
        context.Response.Headers[CorrelationHeader] = correlationId;
        var logger = loggers.CreateLogger(typeof(BookingDraftEndpoint));
        var keys = new Dictionary<string, object?> { ["correlationId"] = correlationId, ["flow"] = Flow };
        if (context.Request.Headers[ShipperHeader].ToString() is { Length: > 0 } shipperId)
        {
            keys["partyId"] = shipperId;
        }

        using var trail = logger.BeginScope(keys);

        var settings = options.Value;
        var answer = settings.Enabled
            ? await BookingDraftOperation.DraftAsync(extractor, context.Request.Body, settings.MaxInputCharacters, correlationId, context.RequestAborted)
            : BookingDraftOperation.Refuse(correlationId, StatusCodes.Status503ServiceUnavailable, BookingDraftOperation.AgentsDisabled, "The assistant is unavailable.");

        var outcome = new Dictionary<string, object?> { ["outcome"] = answer.Outcome };
        if (answer.FailureCode is { } failureCode)
        {
            outcome["failureCode"] = failureCode;
        }

        using var scope = logger.BeginScope(outcome);
        if (answer.Outcome == BookingDraftOperation.Succeeded)
        {
            LogDrafted(logger);
        }
        else if (answer.Outcome == BookingDraftOperation.Refused)
        {
            LogRefused(logger, answer.StatusCode, answer.FailureCode);
        }
        else
        {
            LogFailed(logger, answer.FailureCode);
        }

        return TypedResults.Content(answer.Body.ToJsonString(), "application/json", statusCode: answer.StatusCode);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Answered with a Booking draft")]
    private static partial void LogDrafted(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Refused with {StatusCode} ({FailureCode})")]
    private static partial void LogRefused(ILogger logger, int statusCode, string? failureCode);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The assistant failed ({FailureCode})")]
    private static partial void LogFailed(ILogger logger, string? failureCode);
}
