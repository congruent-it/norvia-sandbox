using Mediator;
using Microsoft.AspNetCore.Http.HttpResults;
using Norvia.Erp.Api.Tracking;
using Norvia.Erp.Application;
using Norvia.Erp.Application.CarrierCosts.Record;
using Norvia.Erp.Application.IntegrationEvents;
using Norvia.Erp.Domain.CarrierCosts;

namespace Norvia.Erp.Api.CarrierCosts;

public static class CarrierCostEndpoints
{
    /// <summary>The app role only the Carrier cost Logic App's managed identity holds, so nothing bypasses the integration layer.</summary>
    public const string RecordRole = "CarrierCosts.Record";

    public static IEndpointRouteBuilder MapCarrierCosts(this IEndpointRouteBuilder app)
    {
        var carrierCosts = app.NewVersionedApi("CarrierCosts")
            .MapGroup("/v{version:apiVersion}/carrier-costs")
            .HasApiVersion(1);

        carrierCosts.MapPost("/", RecordAsync)
            .WithName("RecordCarrierCost")
            .RequireAuthorization(policy => policy.RequireRole(RecordRole))
            .WithMetadata(new TrackedFlow(Flows.CarrierCosts))
            .WithSummary("Record the Carrier cost a CarrierCostIncurred carries, as Open.")
            .WithDescription(
                "The body is the Canonical Model's CarrierCostIncurred, version 1, and nothing else. Idempotent on the Carrier booking number: "
                + "a repeat with the same content answers 200 with the original Carrier cost and repeat true; the first recording answers 201. "
                + "Publishes nothing. Refusals carry a stable code: CarrierNotFound (422), CarrierCostConflict (409, the Carrier booking is "
                + "already recorded with different content), CarrierCostWithoutChargeLines and CarrierCostTotalMismatch (400).");

        return app;
    }

    internal static async Task<Results<Ok<RecordCarrierCostResult>, Created<RecordCarrierCostResult>, ProblemHttpResult, ValidationProblem>> RecordAsync(
        CarrierCostIncurredRequest? request,
        IConflictRetrier retrier,
        CancellationToken cancellationToken)
    {
        var (data, errors) = CarrierCostIncurredValidator.Validate(request);
        if (data is null)
        {
            return TypedResults.ValidationProblem(errors);
        }

        var result = await retrier.RunAsync(mediator => mediator.Send(new RecordCarrierCostCommand(data), cancellationToken).AsTask());
        if (!result.IsSuccess)
        {
            return Refused(result, data.CorrelationId);
        }

        return result.Value.Repeat
            ? TypedResults.Ok(result.Value)
            : TypedResults.Created($"/v1/carrier-costs/{data.CarrierBookingNumber}", result.Value);
    }

    private static ProblemHttpResult Refused(Ardalis.Result.Result<RecordCarrierCostResult> result, string correlationId) =>
        RefusalProblem.Of<CarrierCostRefusalCode, RecordCarrierCostResult>(
            result,
            "The Carrier cost can't be recorded.",
            correlationId,
            code => code switch
            {
                CarrierCostRefusalCode.CarrierNotFound => StatusCodes.Status422UnprocessableEntity,
                CarrierCostRefusalCode.CarrierCostConflict => StatusCodes.Status409Conflict,
                _ => StatusCodes.Status400BadRequest,
            });
}
