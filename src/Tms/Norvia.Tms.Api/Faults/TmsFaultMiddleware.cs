using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Norvia.ServiceDefaults.Faults;
using Norvia.Tms.Application.IntegrationEvents;

namespace Norvia.Tms.Api.Faults;

/// <summary>
/// The gate of the TMS's faults: while one is on, the calls it breaks are answered with a 503 and nothing is done. It sits behind
/// <see cref="Tracking.TrackingScopeMiddleware"/>, so the refused call is still on its Message trail, and the hop it caused is <c>Failed</c> with the
/// fault's name as its <c>fault</c> (coding guidelines §7). The fault endpoints themselves, the health endpoints and the OpenAPI documents are never
/// gated, so a fault can always be switched off, and the platform can still see the application is up.
/// </summary>
public sealed partial class TmsFaultMiddleware(RequestDelegate next, IFaults faults, ILogger<TmsFaultMiddleware> logger)
{
    private static readonly PathString Api = new("/v1");
    private static readonly PathString FaultSwitches = new("/v1/faults");
    private static readonly PathString TrackingEvents = new("/v1/tracking-events");
    private static readonly PathString ShipperTracking = new("/v1/shipper-tracking");

    public async Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (FaultOf(context.Request.Path) is not { } fault)
        {
            await next(context);
            return;
        }

        var keys = new Dictionary<string, object?>
        {
            [TrackingKeys.Outcome] = "Failed",
            [TrackingKeys.FailureCode] = fault.FailureCode,
            [TrackingKeys.Fault] = fault.Name,
        };
        Activity.Current?.SetTag(TrackingKeys.Fault, fault.Name);
        using (logger.BeginScope(keys))
        {
            LogUnavailable(logger, context.Request.Method, context.Request.Path);
        }

        context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
        await context.Response.WriteAsJsonAsync(
            new ProblemDetails { Status = StatusCodes.Status503ServiceUnavailable, Title = fault.Title },
            options: null,
            contentType: "application/problem+json",
            cancellationToken: context.RequestAborted);
    }

    // The most specific fault first, so the hop names the fault the presenter switched on for that API.
    private (string Name, string FailureCode, string Title)? FaultOf(PathString path)
    {
        if (!path.StartsWithSegments(Api) || path.StartsWithSegments(FaultSwitches))
        {
            return null;
        }

        if (path.StartsWithSegments(TrackingEvents) && faults.IsOn(TmsFaultNames.TrackingEventApiFailing))
        {
            return (TmsFaultNames.TrackingEventApiFailing, "TrackingEventApiUnavailable", "The TMS Tracking event API is unavailable.");
        }

        if (path.StartsWithSegments(ShipperTracking) && faults.IsOn(TmsFaultNames.ShipperTrackingApiFailing))
        {
            return (TmsFaultNames.ShipperTrackingApiFailing, "ShipperTrackingApiUnavailable", "The TMS Shipper tracking API is unavailable.");
        }

        return faults.IsOn(TmsFaultNames.Unavailable) ? (TmsFaultNames.Unavailable, "TmsUnavailable", "The TMS is unavailable.") : null;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "{Method} {Path} answered 503 (fault)")]
    private static partial void LogUnavailable(ILogger logger, string method, PathString path);
}
