using Asp.Versioning;
using Mediator;
using Norvia.DriverApp.Application.Settings.GetCurrent;
using Norvia.DriverApp.Application.Settings.SetUnavailable;

namespace Norvia.DriverApp.Api.Settings;

public static class SettingsEndpoints
{
    public static IEndpointRouteBuilder MapSettings(this IEndpointRouteBuilder app)
    {
        var settings = app.NewVersionedApi("Settings")
            .MapGroup("/v{version:apiVersion}/settings")
            .HasApiVersion(1);
        settings.MapGet("/unavailable", GetUnavailableAsync).WithName("GetDriverAppUnavailable");
        settings.MapPut("/unavailable", SetUnavailableAsync).WithName("SetDriverAppUnavailable");

        return app;
    }

    private static async Task<IResult> GetUnavailableAsync(IMediator mediator, CancellationToken cancellationToken) =>
        Results.Ok(await mediator.Send(new GetSettingsQuery(), cancellationToken));

    private static async Task<IResult> SetUnavailableAsync(UnavailableRequest request, IMediator mediator, CancellationToken cancellationToken)
    {
        await mediator.Send(new SetUnavailableCommand(request.Unavailable), cancellationToken);
        return Results.NoContent();
    }
}
