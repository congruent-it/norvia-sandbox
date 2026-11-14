using Mediator;
using Microsoft.AspNetCore.Http.HttpResults;
using Norvia.Tms.Api.MasterData;
using Norvia.Tms.Api.Tracking;
using Norvia.Tms.Application.IntegrationEvents;
using Norvia.Tms.Application.RateCards.Replace;
using Norvia.Tms.Application.RateCards.ReplaceCarrier;

namespace Norvia.Tms.Api.RateCards;

public static class RateCardEndpoints
{
    public static IEndpointRouteBuilder MapRateCards(this IEndpointRouteBuilder app)
    {
        var rateCards = app.NewVersionedApi("RateCards")
            .MapGroup("/v{version:apiVersion}/rate-cards")
            .HasApiVersion(1);

        rateCards.MapPut("/{id}", ReplaceAsync)
            .WithName("ReplaceRateCard")
            .RequireAuthorization(policy => policy.RequireRole(MasterDataRoles.Replace))
            .WithMetadata(new TrackedFlow(Flows.MasterData))
            .WithSummary("Replace the TMS's copy of a Rate Card with the ERP's record.")
            .WithDescription(
                "Upserts the whole Rate Card, so a new one appears, and replaces its list of dated versions. Idempotent on the Master-data revision: a revision "
                + "that is not higher than the copy's answers 200 with applied false and changes nothing. A replaced Rate Card changes only what is rated after it "
                + "arrives. A record the TMS can't read is refused with 400 and the code MasterDataUnreadable; versions that overlap in time with RateCardVersionsOverlap.");

        var carrierRateCards = app.NewVersionedApi("RateCards")
            .MapGroup("/v{version:apiVersion}/carrier-rate-cards")
            .HasApiVersion(1);

        carrierRateCards.MapPut("/{id}", ReplaceCarrierAsync)
            .WithName("ReplaceCarrierRateCard")
            .RequireAuthorization(policy => policy.RequireRole(MasterDataRoles.Replace))
            .WithMetadata(new TrackedFlow(Flows.MasterData))
            .WithSummary("Replace the TMS's copy of a Carrier Rate Card with the ERP's record.")
            .WithDescription(
                "Upserts the whole Carrier Rate Card, so a new one appears, and replaces its list of dated versions. Idempotent on the Master-data revision: a revision "
                + "that is not higher than the copy's answers 200 with applied false and changes nothing. A record the TMS can't read is refused with 400 and the code "
                + "MasterDataUnreadable; versions that overlap in time with RateCardVersionsOverlap.");

        return app;
    }

    internal static async Task<Results<Ok<ReplaceRateCardResult>, ProblemHttpResult>> ReplaceAsync(
        string id, ReplaceRateCardRequest request, IMediator mediator, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var result = await mediator.Send(new ReplaceRateCardCommand(id, request.ShipperId, request.Revision, request.Versions), cancellationToken);
        if (result.IsSuccess)
        {
            return TypedResults.Ok(result.Value);
        }

        var refusal = result.ValidationErrors.First();
        return TypedResults.Problem(
            statusCode: StatusCodes.Status400BadRequest,
            title: "The Rate Card can't be replaced.",
            detail: refusal.ErrorMessage,
            extensions: new Dictionary<string, object?> { ["code"] = refusal.ErrorCode });
    }

    internal static async Task<Results<Ok<ReplaceCarrierRateCardResult>, ProblemHttpResult>> ReplaceCarrierAsync(
        string id, ReplaceCarrierRateCardRequest request, IMediator mediator, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var result = await mediator.Send(new ReplaceCarrierRateCardCommand(id, request.CarrierId, request.Revision, request.Versions), cancellationToken);
        if (result.IsSuccess)
        {
            return TypedResults.Ok(result.Value);
        }

        var refusal = result.ValidationErrors.First();
        return TypedResults.Problem(
            statusCode: StatusCodes.Status400BadRequest,
            title: "The Carrier Rate Card can't be replaced.",
            detail: refusal.ErrorMessage,
            extensions: new Dictionary<string, object?> { ["code"] = refusal.ErrorCode });
    }
}
