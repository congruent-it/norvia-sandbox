using Mediator;
using Microsoft.AspNetCore.Mvc;
using Norvia.DriverApp.Application.Drivers.List;
using Norvia.DriverApp.Application.Settings.SetUnavailable;

namespace Norvia.DriverApp.Web.Sessions;

/// <summary>The form posts of the Driver app: signing in, signing out, and the demo setting. Plain posts, so they work before a page is interactive.</summary>
public static class SessionEndpoints
{
    public static IEndpointRouteBuilder MapSession(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/sign-in", async ([FromForm] string driver, HttpResponse response, IMediator mediator, CancellationToken cancellationToken) =>
        {
            var drivers = await mediator.Send(new ListDriversQuery(), cancellationToken);
            if (DriverSession.Find(drivers, driver) is not { } chosen)
            {
                // With a body, so the status code pages don't turn it into the Not Found page.
                return TypedResults.Problem($"There is no Driver '{driver}'.", statusCode: StatusCodes.Status400BadRequest, title: "Unknown Driver");
            }

            DriverSession.SignIn(response, chosen);
            return (IResult)TypedResults.Redirect("/");
        });

        endpoints.MapPost("/sign-out", (HttpResponse response) =>
        {
            DriverSession.SignOut(response);
            return TypedResults.Redirect("/sign-in");
        });

        endpoints.MapPost("/settings/unavailable", async ([FromForm] bool unavailable, IMediator mediator, CancellationToken cancellationToken) =>
        {
            await mediator.Send(new SetUnavailableCommand(unavailable), cancellationToken);
            return TypedResults.Redirect("/settings");
        });

        return endpoints;
    }
}
