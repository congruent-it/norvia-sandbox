using Mediator;
using Microsoft.AspNetCore.Http.HttpResults;
using Norvia.PartnerPortal.Application.ConsigneeTracking.GetView;
using Norvia.PartnerPortal.Application.ShipperTracking;

namespace Norvia.PartnerPortal.Web.ConsigneeTracking;

/// <summary>
/// The Consignee's tracking link, <c>/track/{code}</c> (ADR 0052). It sits outside the Entra gate of ADR 0024, as nobody signs in as a Consignee, and
/// reads no Persona and no signed-in user: the code in the link opens one Transport Order and nothing else. It is a page of its own with no
/// scripts and no stylesheet to fetch, as everything but this path is behind the gate. The code is a credential, so the response says
/// <c>Referrer-Policy: no-referrer</c> and <c>Cache-Control: no-store</c>, and the code is never logged.
/// </summary>
public static class ConsigneeTrackingEndpoint
{
    public const string Route = "/track/{code}";

    public static IEndpointRouteBuilder MapConsigneeTrackingEndpoint(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet(Route, TrackAsync).AllowAnonymous();
        return endpoints;
    }

    public static async Task<RazorComponentResult<ConsigneeTrackingPage>> TrackAsync(
        string code, HttpContext context, IMediator mediator, TimeProvider time, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(mediator);
        ArgumentNullException.ThrowIfNull(time);

        context.Response.Headers["Referrer-Policy"] = "no-referrer";
        context.Response.Headers.CacheControl = "no-store";
        context.Response.Headers["X-Robots-Tag"] = "noindex";

        var answer = await mediator.Send(new GetConsigneeTrackingQuery(code, ClientIpOf(context)), cancellationToken);
        return new RazorComponentResult<ConsigneeTrackingPage>(new { Answer = answer, FetchedAt = time.GetUtcNow() })
        {
            StatusCode = StatusOf(answer.Outcome),
        };
    }

    // The Consignee's own address: the first of X-Forwarded-For when a proxy such as Container Apps' ingress set it, otherwise the connection's.
    private static string? ClientIpOf(HttpContext context) =>
        context.Request.Headers["X-Forwarded-For"].ToString().Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()
        ?? context.Connection.RemoteIpAddress?.ToString();

    // A wrong, expired and replaced code are one answer: the same status and the same page.
    private static int StatusOf(TrackingOutcome outcome) => outcome switch
    {
        TrackingOutcome.Found => StatusCodes.Status200OK,
        TrackingOutcome.NotFound => StatusCodes.Status404NotFound,
        TrackingOutcome.Refused => StatusCodes.Status429TooManyRequests,
        _ => StatusCodes.Status503ServiceUnavailable,
    };
}
