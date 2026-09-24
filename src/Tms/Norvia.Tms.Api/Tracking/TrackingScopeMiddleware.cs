using System.Diagnostics;

namespace Norvia.Tms.Api.Tracking;

/// <summary>
/// Puts <c>correlationId</c> and <c>flow</c> in the logging scope and on the request's trace for the whole of a call to
/// a tracked endpoint, before authentication, so every trace of it joins its Message trail (coding guidelines §7). A
/// refused call is logged too, with the outcome <c>Refused</c>, as it never reaches the endpoint.
/// </summary>
public sealed partial class TrackingScopeMiddleware(RequestDelegate next, ILogger<TrackingScopeMiddleware> logger)
{
    public const string CorrelationIdHeader = "X-Correlation-ID";

    public async Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.GetEndpoint()?.Metadata.GetMetadata<TrackedFlow>() is not { } flow)
        {
            await next(context);
            return;
        }

        var trail = TrailOf(context, flow);
        foreach (var (key, value) in trail)
        {
            Activity.Current?.SetTag(key, value);
        }

        using var scope = logger.BeginScope(trail);
        await next(context);

        if (context.Response.StatusCode is StatusCodes.Status401Unauthorized or StatusCodes.Status403Forbidden)
        {
            using var refused = logger.BeginScope(new Dictionary<string, object?> { ["outcome"] = "Refused" });
            LogRefused(logger, context.Response.StatusCode);
        }
    }

    // A key that doesn't apply is left out, never sent empty.
    private static Dictionary<string, object?> TrailOf(HttpContext context, TrackedFlow flow)
    {
        var trail = new Dictionary<string, object?> { ["flow"] = flow.Name };
        var correlationId = context.Request.Headers[CorrelationIdHeader].ToString();
        if (!string.IsNullOrWhiteSpace(correlationId))
        {
            trail["correlationId"] = correlationId;
        }

        return trail;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Call refused with {StatusCode}")]
    private static partial void LogRefused(ILogger logger, int statusCode);
}
