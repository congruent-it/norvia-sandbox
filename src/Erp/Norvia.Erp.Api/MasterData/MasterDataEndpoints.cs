using Mediator;
using Microsoft.AspNetCore.Http.HttpResults;
using Norvia.Erp.Api.Tracking;
using Norvia.Erp.Application;
using Norvia.Erp.Application.IntegrationEvents;
using Norvia.Erp.Application.MasterData;
using Norvia.Erp.Application.MasterData.Carriers.Save;
using Norvia.Erp.Application.MasterData.RateCards;
using Norvia.Erp.Application.MasterData.RateCards.Save;
using Norvia.Erp.Application.MasterData.RateCards.SaveCarrier;
using Norvia.Erp.Application.MasterData.Shippers.Save;

namespace Norvia.Erp.Api.MasterData;

public static class MasterDataEndpoints
{
    /// <summary>The app role of the ERP UI's own identity, which is how Finance's edits reach the ERP API (ADR 0028's split, for the ERP).</summary>
    public const string ManageRole = "MasterData.Manage";

    public static IEndpointRouteBuilder MapMasterData(this IEndpointRouteBuilder app)
    {
        var shippers = app.NewVersionedApi("MasterData")
            .MapGroup("/v{version:apiVersion}/shippers")
            .HasApiVersion(1);

        shippers.MapPut("/{id}", SaveShipperAsync)
            .WithName("SaveShipper")
            .RequireAuthorization(policy => policy.RequireRole(ManageRole))
            .WithMetadata(new TrackedFlow(Flows.MasterData))
            .WithSummary("Create or edit a Shipper, and replicate it to the TMS.")
            .WithDescription(
                "What Finance's Shipper form does, through the same command. Creates the Shipper (201, Master-data revision 1) or replaces its name and "
                + "active flag (200, the revision raised by one) and publishes the whole Shipper as the Shipper event, in one transaction. Idempotent on content: "
                + "a repeat of what the Shipper already is answers 200 with the unchanged revision and publishes nothing. A Shipper is never deleted, only made inactive. "
                + "Refusals carry a stable code (400): MasterDataFieldRequired and MasterDataFieldTooLong.");

        var carriers = app.NewVersionedApi("MasterData")
            .MapGroup("/v{version:apiVersion}/carriers")
            .HasApiVersion(1);

        carriers.MapPut("/{id}", SaveCarrierAsync)
            .WithName("SaveCarrier")
            .RequireAuthorization(policy => policy.RequireRole(ManageRole))
            .WithMetadata(new TrackedFlow(Flows.MasterData))
            .WithSummary("Create or edit a Carrier, and replicate it to the TMS.")
            .WithDescription(
                "What Finance's Carrier form does, through the same command. Creates the Carrier (201, Master-data revision 1) or replaces its name and "
                + "active flag (200, the revision raised by one) and publishes the whole Carrier as the Carrier event, in one transaction. Idempotent on content: "
                + "a repeat of what the Carrier already is answers 200 with the unchanged revision and publishes nothing. A Carrier is never deleted, only made inactive. "
                + "Refusals carry a stable code (400): MasterDataFieldRequired and MasterDataFieldTooLong.");

        var rateCards = app.NewVersionedApi("MasterData")
            .MapGroup("/v{version:apiVersion}/rate-cards")
            .HasApiVersion(1);

        rateCards.MapPut("/{id}", SaveRateCardAsync)
            .WithName("SaveRateCard")
            .RequireAuthorization(policy => policy.RequireRole(ManageRole))
            .WithMetadata(new TrackedFlow(Flows.MasterData))
            .WithSummary("Create or edit a Shipper's Rate Card, and replicate it to the TMS.")
            .WithDescription(
                "Creates the Rate Card (201, Master-data revision 1) or replaces its whole list of dated versions (200, the revision raised by one) and publishes "
                + "the whole Rate Card as the RateCard event, in one transaction. Idempotent on content: a repeat of what the card already is answers 200 with the "
                + "unchanged revision and publishes nothing. Versions must not overlap in time, and a Shipper has one Rate Card. Refusals carry a stable code (400): "
                + "MasterDataFieldRequired, MasterDataFieldTooLong, RateCardVersionInvalid, RateCardVersionsOverlap, RateCardPartyUnknown and RateCardShipperHasCard.");

        var carrierRateCards = app.NewVersionedApi("MasterData")
            .MapGroup("/v{version:apiVersion}/carrier-rate-cards")
            .HasApiVersion(1);

        carrierRateCards.MapPut("/{id}", SaveCarrierRateCardAsync)
            .WithName("SaveCarrierRateCard")
            .RequireAuthorization(policy => policy.RequireRole(ManageRole))
            .WithMetadata(new TrackedFlow(Flows.MasterData))
            .WithSummary("Create or edit a Carrier's Carrier Rate Card, and replicate it to the TMS.")
            .WithDescription(
                "Creates the Carrier Rate Card (201, Master-data revision 1) or replaces its whole list of dated versions (200, the revision raised by one) and publishes "
                + "the whole card as the CarrierRateCard event, in one transaction. Idempotent on content: a repeat of what the card already is answers 200 with the "
                + "unchanged revision and publishes nothing. Versions must not overlap in time, and a Carrier has one Carrier Rate Card. Refusals carry a stable code (400): "
                + "MasterDataFieldRequired, MasterDataFieldTooLong, RateCardVersionInvalid, RateCardVersionsOverlap, RateCardPartyUnknown and RateCardCarrierHasCard.");

        return app;
    }

    internal static async Task<Results<Ok<SaveShipperResult>, Created<SaveShipperResult>, ProblemHttpResult>> SaveShipperAsync(
        string id,
        SaveShipperRequest request,
        IConflictRetrier retrier,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var result = await retrier.RunAsync(mediator => mediator.Send(new SaveShipperCommand(id, request.Name, request.Active), cancellationToken).AsTask());
        if (!result.IsSuccess)
        {
            var refusal = result.ValidationErrors.First();
            return TypedResults.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "The Shipper can't be saved.",
                detail: refusal.ErrorMessage,
                extensions: new Dictionary<string, object?> { ["code"] = refusal.ErrorCode });
        }

        return result.Value.Outcome == MasterDataSaveOutcome.Created
            ? TypedResults.Created($"/v1/shippers/{id}", result.Value)
            : TypedResults.Ok(result.Value);
    }

    internal static async Task<Results<Ok<SaveCarrierResult>, Created<SaveCarrierResult>, ProblemHttpResult>> SaveCarrierAsync(
        string id,
        SaveCarrierRequest request,
        IConflictRetrier retrier,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var result = await retrier.RunAsync(mediator => mediator.Send(new SaveCarrierCommand(id, request.Name, request.Active), cancellationToken).AsTask());
        if (!result.IsSuccess)
        {
            var refusal = result.ValidationErrors.First();
            return TypedResults.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "The Carrier can't be saved.",
                detail: refusal.ErrorMessage,
                extensions: new Dictionary<string, object?> { ["code"] = refusal.ErrorCode });
        }

        return result.Value.Outcome == MasterDataSaveOutcome.Created
            ? TypedResults.Created($"/v1/carriers/{id}", result.Value)
            : TypedResults.Ok(result.Value);
    }

    internal static async Task<Results<Ok<SaveRateCardResult>, Created<SaveRateCardResult>, ProblemHttpResult>> SaveRateCardAsync(
        string id,
        SaveRateCardRequest request,
        IConflictRetrier retrier,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var result = await retrier.RunAsync(mediator => mediator.Send(new SaveRateCardCommand(id, request.ShipperId, request.Versions), cancellationToken).AsTask());
        if (!result.IsSuccess)
        {
            var refusal = result.ValidationErrors.First();
            return TypedResults.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "The Rate Card can't be saved.",
                detail: refusal.ErrorMessage,
                extensions: new Dictionary<string, object?> { ["code"] = refusal.ErrorCode });
        }

        return result.Value.Outcome == MasterDataSaveOutcome.Created
            ? TypedResults.Created($"/v1/rate-cards/{id}", result.Value)
            : TypedResults.Ok(result.Value);
    }

    internal static async Task<Results<Ok<SaveCarrierRateCardResult>, Created<SaveCarrierRateCardResult>, ProblemHttpResult>> SaveCarrierRateCardAsync(
        string id,
        SaveCarrierRateCardRequest request,
        IConflictRetrier retrier,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var result = await retrier.RunAsync(mediator => mediator.Send(new SaveCarrierRateCardCommand(id, request.CarrierId, request.Versions), cancellationToken).AsTask());
        if (!result.IsSuccess)
        {
            var refusal = result.ValidationErrors.First();
            return TypedResults.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "The Carrier Rate Card can't be saved.",
                detail: refusal.ErrorMessage,
                extensions: new Dictionary<string, object?> { ["code"] = refusal.ErrorCode });
        }

        return result.Value.Outcome == MasterDataSaveOutcome.Created
            ? TypedResults.Created($"/v1/carrier-rate-cards/{id}", result.Value)
            : TypedResults.Ok(result.Value);
    }
}
