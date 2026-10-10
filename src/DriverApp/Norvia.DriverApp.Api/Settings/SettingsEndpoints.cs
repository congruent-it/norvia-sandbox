using Mediator;
using Norvia.DriverApp.Application.Settings.SetUnavailable;

namespace Norvia.DriverApp.Api.Settings;

public static class SettingsEndpoints
{
    public static IEndpointRouteBuilder MapSettings(this IEndpointRouteBuilder app)
    {
        app.MapPut("/v1/settings/unavailable", async (UnavailableRequest request, IMediator mediator, CancellationToken cancellationToken) =>
        {
            await mediator.Send(new SetUnavailableCommand(request.Unavailable), cancellationToken);
            return Results.NoContent();
        });

        return app;
    }
}

internal sealed record UnavailableRequest(bool Unavailable);
