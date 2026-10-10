using Asp.Versioning;
using Mediator;
using Norvia.DriverApp.Application.Settings.SetUnavailable;

namespace Norvia.DriverApp.Api.Settings;

public static class SettingsEndpoints
{
    public static IEndpointRouteBuilder MapSettings(this IEndpointRouteBuilder app)
    {
        app.NewVersionedApi("Settings")
            .MapGroup("/v{version:apiVersion}/settings")
            .HasApiVersion(1)
            .MapPut("/unavailable", SetUnavailableAsync)
            .WithName("SetDriverAppUnavailable");

        return app;
    }

    private static async Task<IResult> SetUnavailableAsync(UnavailableRequest request, IMediator mediator, CancellationToken cancellationToken)
    {
        await mediator.Send(new SetUnavailableCommand(request.Unavailable), cancellationToken);
        return Results.NoContent();
    }
}
