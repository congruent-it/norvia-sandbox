using Ardalis.Result;
using Mediator;
using Microsoft.AspNetCore.Http.HttpResults;
using Norvia.Tms.Api.MasterData;
using Norvia.Tms.Api.Tracking;
using Norvia.Tms.Application.IntegrationEvents;
using Norvia.Tms.Application.Shippers.Replace;

namespace Norvia.Tms.Api.Shippers;

public static class ShipperEndpoints
{
    public static IEndpointRouteBuilder MapShippers(this IEndpointRouteBuilder app)
    {
        var shippers = app.NewVersionedApi("Shippers")
            .MapGroup("/v{version:apiVersion}/shippers")
            .HasApiVersion(1);

        shippers.MapPut("/{id}", ReplaceAsync)
            .WithName("ReplaceShipper")
            .RequireAuthorization(policy => policy.RequireRole(MasterDataRoles.Replace))
            .WithMetadata(new TrackedFlow(Flows.MasterData))
            .WithSummary("Replace the TMS's copy of a Shipper with the ERP's record.")
            .WithDescription(
                "Upserts the whole Shipper, so a new Shipper appears. Idempotent on the Master-data revision: a revision that is not higher than the "
                + "copy's answers 200 with applied false and changes nothing. The TMS never edits a Shipper itself. A record the TMS can't read "
                + "(no name, a revision below 1) is refused with 400 and the code MasterDataUnreadable.");

        return app;
    }

    internal static async Task<Results<Ok<ReplaceShipperResult>, ProblemHttpResult>> ReplaceAsync(
        string id, ReplaceShipperRequest request, IMediator mediator, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var result = await mediator.Send(new ReplaceShipperCommand(id, request.Name, request.Active, request.Revision), cancellationToken);
        if (result.IsSuccess)
        {
            return TypedResults.Ok(result.Value);
        }

        var refusal = result.ValidationErrors.First();
        return TypedResults.Problem(
            statusCode: StatusCodes.Status400BadRequest,
            title: "The Shipper can't be replaced.",
            detail: refusal.ErrorMessage,
            extensions: new Dictionary<string, object?> { ["code"] = refusal.ErrorCode });
    }
}
