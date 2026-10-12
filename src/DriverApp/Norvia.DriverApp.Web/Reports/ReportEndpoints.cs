using System.Security.Claims;
using Mediator;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Http.Features;
using Norvia.DriverApp.Application.Reports.StartTrip;
using Norvia.DriverApp.Web.Drivers;

namespace Norvia.DriverApp.Web.Reports;

/// <summary>The form posts of the Driver's reports. Plain posts, so they work before a page is interactive.</summary>
public static class ReportEndpoints
{
    public static IEndpointRouteBuilder MapReports(this IEndpointRouteBuilder endpoints)
    {
        // The Driver ID comes from the token, never from the request (ADR 0005, ADR 0037): the form carries only which Trip. The post binds no form
        // field, so nothing would act on the antiforgery middleware's verdict; the handler does.
        endpoints.MapPost("/trips/{tripNumber}/start", async (string tripNumber, HttpContext context, IMediator mediator, CancellationToken cancellationToken) =>
        {
            if (context.Features.Get<IAntiforgeryValidationFeature>() is { IsValid: false })
            {
                // With a body, so the status code pages don't re-execute the request as the Not Found page, which the antiforgery check would then fail again.
                return Results.Problem("The form is out of date. Reload My Trips and try again.", statusCode: StatusCodes.Status400BadRequest, title: "Bad request");
            }

            if (context.User.FindFirstValue(DriverClaims.DriverId) is not { } driverId)
            {
                return Results.Forbid();
            }

            var started = await mediator.Send(new StartTripCommand(tripNumber, driverId), cancellationToken);
            return started.IsSuccess ? Results.Redirect("/") : Results.NotFound();
        }).RequireAuthorization(DriverClaims.DriverPolicy).WithMetadata(new RequireAntiforgeryTokenAttribute());

        return endpoints;
    }
}
