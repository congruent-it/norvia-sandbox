using Ardalis.Result;
using Mediator;
using Microsoft.AspNetCore.Http.HttpResults;
using Norvia.Tms.Api.MasterData;
using Norvia.Tms.Api.Tracking;
using Norvia.Tms.Application.Fleet.ReplaceDriver;
using Norvia.Tms.Application.IntegrationEvents;

namespace Norvia.Tms.Api.Fleet;

public static class DriverEndpoints
{
    public static IEndpointRouteBuilder MapDrivers(this IEndpointRouteBuilder app)
    {
        var drivers = app.NewVersionedApi("Drivers")
            .MapGroup("/v{version:apiVersion}/drivers")
            .HasApiVersion(1);

        drivers.MapPut("/{id}", ReplaceAsync)
            .WithName("ReplaceDriver")
            .RequireAuthorization(policy => policy.RequireRole(MasterDataRoles.Replace))
            .WithMetadata(new TrackedFlow(Flows.MasterData))
            .WithSummary("Replace the TMS's copy of a Driver with the ERP's record.")
            .WithDescription(
                "Upserts the whole Driver, so a new Driver appears. Idempotent on the Master-data revision: a revision that is not higher than the "
                + "copy's answers 200 with applied false and changes nothing. The TMS never edits a Driver itself, and replacing one touches no Roster, Absence "
                + "or Trip: a Driver made inactive drops out of the days opened after that, and a Trip already using them is flagged Needs a Driver. "
                + "A record the TMS can't read (no name, a licence other than B, C or CE, a revision below 1) is refused with 400 and the code MasterDataUnreadable.");

        return app;
    }

    internal static async Task<Results<Ok<ReplaceDriverResult>, ProblemHttpResult>> ReplaceAsync(
        string id, ReplaceDriverRequest request, IMediator mediator, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var result = await mediator.Send(new ReplaceDriverCommand(id, request.Name, request.Licences, request.Active, request.Revision), cancellationToken);
        if (result.IsSuccess)
        {
            return TypedResults.Ok(result.Value);
        }

        var refusal = result.ValidationErrors.First();
        return TypedResults.Problem(
            statusCode: StatusCodes.Status400BadRequest,
            title: "The Driver can't be replaced.",
            detail: refusal.ErrorMessage,
            extensions: new Dictionary<string, object?> { ["code"] = refusal.ErrorCode });
    }
}
