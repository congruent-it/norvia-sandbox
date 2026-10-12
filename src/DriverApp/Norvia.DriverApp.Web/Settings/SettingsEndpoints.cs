using Mediator;
using Microsoft.AspNetCore.Mvc;
using Norvia.DriverApp.Application.Settings.SetUnavailable;
using Norvia.DriverApp.Web.Drivers;

namespace Norvia.DriverApp.Web.Settings;

/// <summary>The form post of the demo setting. A plain post, so it works before a page is interactive.</summary>
public static class SettingsEndpoints
{
    public static IEndpointRouteBuilder MapSettings(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/settings/unavailable", async ([FromForm] bool unavailable, IMediator mediator, CancellationToken cancellationToken) =>
        {
            await mediator.Send(new SetUnavailableCommand(unavailable), cancellationToken);
            return TypedResults.Redirect("/settings");
        }).RequireAuthorization(DriverClaims.DriverPolicy);

        return endpoints;
    }
}
