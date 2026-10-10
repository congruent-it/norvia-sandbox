using Ardalis.Result;
using Asp.Versioning;
using Mediator;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Norvia.DriverApp.Application;
using Norvia.DriverApp.Application.Trips;
using Norvia.DriverApp.Application.Trips.GetForDriver;
using IResult = Microsoft.AspNetCore.Http.IResult;

namespace Norvia.DriverApp.Api.Trips;

public static class TripEndpoints
{
    public static IEndpointRouteBuilder MapTrips(this IEndpointRouteBuilder app)
    {
        // The Trip endpoint asks its caller, the Trip dispatch Logic App, for the endpoint's credential. Commands are idempotent: a version sent twice, or a lower one after a higher, is acknowledged and ignored.
        app.NewVersionedApi("Trips")
            .MapGroup("/v{version:apiVersion}/trips")
            .HasApiVersion(1)
            .MapPost("/", ReceiveAsync)
            .WithName("ReceiveTripVersion");

        app.NewVersionedApi("Drivers")
            .MapGroup("/v{version:apiVersion}/drivers")
            .HasApiVersion(1)
            .MapGet("/{driverId}/trips", ListForDriverAsync)
            .WithName("ListTripsOfDriver");

        return app;
    }

    private static async Task<IResult> ReceiveAsync(
        TripVersionRequest request,
        [FromHeader(Name = EndpointCredential.ApiKeyHeader)] string? apiKey,
        IOptions<DriverEndpointOptions> options,
        IConflictRetrier retrier,
        CancellationToken cancellationToken)
    {
        if (!EndpointCredential.IsValid(apiKey, options.Value.ApiKey))
        {
            return Results.Problem(statusCode: StatusCodes.Status401Unauthorized, title: "The Trip endpoint needs the Driver app's endpoint credential.");
        }

        var command = request.ToCommand();
        var result = await retrier.RunAsync(mediator => mediator.Send(command, cancellationToken).AsTask());
        return result.Status switch
        {
            ResultStatus.Ok => Results.Ok(new TripVersionAccepted(result.Value)),
            ResultStatus.Invalid => Results.ValidationProblem(result.ValidationErrors
                .GroupBy(error => error.Identifier)
                .ToDictionary(errors => errors.Key, errors => errors.Select(error => error.ErrorMessage).ToArray())),
            _ => Results.Problem(statusCode: StatusCodes.Status503ServiceUnavailable, title: "The Driver app is unavailable."),
        };
    }

    private static async Task<IResult> ListForDriverAsync(string driverId, IMediator mediator, CancellationToken cancellationToken) =>
        Results.Ok(await mediator.Send(new GetDriverTripsQuery(driverId), cancellationToken));
}
