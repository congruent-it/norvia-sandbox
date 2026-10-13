using System.Security.Claims;
using Mediator;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Norvia.DriverApp.Application.Reports.Deliver;
using Norvia.DriverApp.Application.Reports.ReportStop;
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
            if (Reject(context, out var driverId) is { } rejected)
            {
                return rejected;
            }

            var started = await mediator.Send(new StartTripCommand(tripNumber, driverId), cancellationToken);
            return started.IsSuccess ? Results.Redirect("/") : Results.NotFound();
        }).RequireAuthorization(DriverClaims.DriverPolicy).WithMetadata(new RequireAntiforgeryTokenAttribute());

        // Deliver: the Proof of delivery form, with a signature and photos. Its own route, as the form carries files and fields the other reports don't.
        endpoints.MapPost("/trips/{tripNumber}/stops/{shipmentNumber}/delivery/deliver", async (
            string tripNumber, string shipmentNumber, HttpContext context, IMediator mediator, CancellationToken cancellationToken) =>
        {
            if (Reject(context, out var driverId) is { } rejected)
            {
                return rejected;
            }

            var screen = StopRoutes.Screen(tripNumber, shipmentNumber, Application.Trips.StopKindDto.Delivery);
            var form = await context.Request.ReadFormAsync(cancellationToken);
            if (await ProofOfDeliveryForm.ReadAsync(form, cancellationToken) is not { } proof)
            {
                // A file over the limit is not read: the Driver app would only refuse it.
                return Results.Redirect($"{screen}?problem=photos");
            }

            var delivered = await mediator.Send(new DeliverCommand(tripNumber, driverId, shipmentNumber, proof.ReceivedBy, proof.Condition, proof.Remark, proof.Signature, proof.Photos), cancellationToken);
            return delivered.Status switch
            {
                Ardalis.Result.ResultStatus.Ok => Results.Redirect("/"),
                Ardalis.Result.ResultStatus.NotFound => Results.NotFound(),
                Ardalis.Result.ResultStatus.Invalid => Results.Redirect($"{screen}?problem={Uri.EscapeDataString(delivered.ValidationErrors.First().Identifier)}"),
                _ => Results.Redirect(screen),
            };
        }).RequireAuthorization(DriverClaims.DriverPolicy).WithMetadata(new RequireAntiforgeryTokenAttribute(), new RequestSizeLimitAttribute(ProofOfDeliveryForm.MaximumRequestBytes), new RequestFormLimitsAttribute { MultipartBodyLengthLimit = ProofOfDeliveryForm.MaximumRequestBytes });

        // Collected, Collection failed, Delivery failed or Report damage at a Stop. The form carries the remark and nothing else the Driver app trusts.
        endpoints.MapPost("/trips/{tripNumber}/stops/{shipmentNumber}/{kind}/{action}", async (
            string tripNumber, string shipmentNumber, string kind, string action, HttpContext context, IMediator mediator, CancellationToken cancellationToken) =>
        {
            if (Reject(context, out var driverId) is { } rejected)
            {
                return rejected;
            }

            if (!StopRoutes.TryParse(kind, out Application.Trips.StopKindDto stop) || !StopRoutes.TryParse(action, out Application.Reports.StopActionDto what))
            {
                return Results.NotFound();
            }

            var form = await context.Request.ReadFormAsync(cancellationToken);
            var reported = await mediator.Send(new ReportStopCommand(tripNumber, driverId, shipmentNumber, stop, what, form["remark"].ToString()), cancellationToken);
            var screen = StopRoutes.Screen(tripNumber, shipmentNumber, stop);
            return reported.Status switch
            {
                Ardalis.Result.ResultStatus.Ok => Results.Redirect("/"),
                Ardalis.Result.ResultStatus.NotFound => Results.NotFound(),
                Ardalis.Result.ResultStatus.Invalid => Results.Redirect($"{screen}?problem={Uri.EscapeDataString(reported.ValidationErrors.First().Identifier)}"),

                // The Stop screen says why the Stop is not available now.
                _ => Results.Redirect(screen),
            };
        }).RequireAuthorization(DriverClaims.DriverPolicy).WithMetadata(new RequireAntiforgeryTokenAttribute());

        return endpoints;
    }

    /// <summary>The refusal of a post that is out of date or isn't a Driver's; null, with the Driver ID from the token, when it can go on.</summary>
    private static IResult? Reject(HttpContext context, out string driverId)
    {
        driverId = string.Empty;
        if (context.Features.Get<IAntiforgeryValidationFeature>() is { IsValid: false })
        {
            // With a body, so the status code pages don't re-execute the request as the Not Found page, which the antiforgery check would then fail again.
            return Results.Problem("The form is out of date. Reload My Trips and try again.", statusCode: StatusCodes.Status400BadRequest, title: "Bad request");
        }

        if (context.User.FindFirstValue(DriverClaims.DriverId) is not { } found)
        {
            return Results.Forbid();
        }

        driverId = found;
        return null;
    }
}
