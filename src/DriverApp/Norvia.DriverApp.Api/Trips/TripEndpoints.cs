using Ardalis.Result;
using Mediator;
using Norvia.DriverApp.Application.Trips.GetForDriver;
using Norvia.DriverApp.Application.Trips.Receive;

namespace Norvia.DriverApp.Api.Trips;

public static class TripEndpoints
{
    public static IEndpointRouteBuilder MapTrips(this IEndpointRouteBuilder app)
    {
        app.MapPost("/v1/trips", async (TripVersionRequest request, IMediator mediator, CancellationToken cancellationToken) =>
        {
            if (!request.TryToCommand(out var command))
            {
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["plannedDrivingDuration"] = ["Must be an ISO 8601 duration, such as PT6H."] });
            }

            var result = await mediator.Send(command, cancellationToken);
            return result.Status switch
            {
                ResultStatus.Unavailable => Results.Problem(statusCode: StatusCodes.Status503ServiceUnavailable, title: "The Driver app is unavailable."),
                _ => Results.Ok(new { outcome = result.Value }),
            };
        });

        app.MapGet("/v1/drivers/{driverId}/trips", async (string driverId, IMediator mediator, CancellationToken cancellationToken) =>
            Results.Ok(await mediator.Send(new GetDriverTripsQuery(driverId), cancellationToken)));

        return app;
    }
}
