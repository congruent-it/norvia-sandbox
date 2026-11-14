using Ardalis.Result;
using Mediator;
using Microsoft.AspNetCore.Http.HttpResults;
using Norvia.Tms.Api.MasterData;
using Norvia.Tms.Api.Tracking;
using Norvia.Tms.Application.Fleet.ReplaceVehicle;
using Norvia.Tms.Application.IntegrationEvents;

namespace Norvia.Tms.Api.Fleet;

public static class VehicleEndpoints
{
    public static IEndpointRouteBuilder MapVehicles(this IEndpointRouteBuilder app)
    {
        var vehicles = app.NewVersionedApi("Vehicles")
            .MapGroup("/v{version:apiVersion}/vehicles")
            .HasApiVersion(1);

        vehicles.MapPut("/{id}", ReplaceAsync)
            .WithName("ReplaceVehicle")
            .RequireAuthorization(policy => policy.RequireRole(MasterDataRoles.Replace))
            .WithMetadata(new TrackedFlow(Flows.MasterData))
            .WithSummary("Replace the TMS's copy of a Vehicle with the ERP's record.")
            .WithDescription(
                "Upserts the whole Vehicle, so a new Vehicle appears. Idempotent on the Master-data revision: a revision that is not higher than the "
                + "copy's answers 200 with applied false and changes nothing. The TMS never edits a Vehicle itself, and replacing one touches no Off the road day "
                + "or Trip: a Vehicle made inactive drops out of the days opened after that, and a Trip already using it is flagged Needs a Vehicle. "
                + "A record the TMS can't read (no registration, an unknown type, no payload, a revision below 1) is refused with 400 and the code MasterDataUnreadable.");

        return app;
    }

    internal static async Task<Results<Ok<ReplaceVehicleResult>, ProblemHttpResult>> ReplaceAsync(
        string id, ReplaceVehicleRequest request, IMediator mediator, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var result = await mediator.Send(
            new ReplaceVehicleCommand(id, request.Registration, request.Type, request.PayloadKg, request.PalletPlaces, request.Active, request.Revision), cancellationToken);
        if (result.IsSuccess)
        {
            return TypedResults.Ok(result.Value);
        }

        var refusal = result.ValidationErrors.First();
        return TypedResults.Problem(
            statusCode: StatusCodes.Status400BadRequest,
            title: "The Vehicle can't be replaced.",
            detail: refusal.ErrorMessage,
            extensions: new Dictionary<string, object?> { ["code"] = refusal.ErrorCode });
    }
}
