using Ardalis.Result;
using Mediator;
using Microsoft.AspNetCore.Http.HttpResults;
using Norvia.Tms.Api.MasterData;
using Norvia.Tms.Api.Tracking;
using Norvia.Tms.Application.Carriers.Replace;
using Norvia.Tms.Application.IntegrationEvents;

namespace Norvia.Tms.Api.Carriers;

public static class CarrierEndpoints
{
    public static IEndpointRouteBuilder MapCarriers(this IEndpointRouteBuilder app)
    {
        var carriers = app.NewVersionedApi("Carriers")
            .MapGroup("/v{version:apiVersion}/carriers")
            .HasApiVersion(1);

        carriers.MapPut("/{id}", ReplaceAsync)
            .WithName("ReplaceCarrier")
            .RequireAuthorization(policy => policy.RequireRole(MasterDataRoles.Replace))
            .WithMetadata(new TrackedFlow(Flows.MasterData))
            .WithSummary("Replace the TMS's copy of a Carrier with the ERP's record.")
            .WithDescription(
                "Upserts the whole Carrier, so a new Carrier appears. Idempotent on the Master-data revision: a revision that is not higher than the "
                + "copy's answers 200 with applied false and changes nothing. The TMS never edits a Carrier itself. A record the TMS can't read "
                + "(no name, a revision below 1) is refused with 400 and the code MasterDataUnreadable.");

        return app;
    }

    internal static async Task<Results<Ok<ReplaceCarrierResult>, ProblemHttpResult>> ReplaceAsync(
        string id, ReplaceCarrierRequest request, IMediator mediator, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var result = await mediator.Send(new ReplaceCarrierCommand(id, request.Name, request.Active, request.Revision), cancellationToken);
        if (result.IsSuccess)
        {
            return TypedResults.Ok(result.Value);
        }

        var refusal = result.ValidationErrors.First();
        return TypedResults.Problem(
            statusCode: StatusCodes.Status400BadRequest,
            title: "The Carrier can't be replaced.",
            detail: refusal.ErrorMessage,
            extensions: new Dictionary<string, object?> { ["code"] = refusal.ErrorCode });
    }
}
