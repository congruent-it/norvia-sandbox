using Mediator;
using Microsoft.AspNetCore.Http.HttpResults;
using Norvia.Erp.Api.Tracking;
using Norvia.Erp.Application;
using Norvia.Erp.Application.BillableLines;
using Norvia.Erp.Application.BillableLines.Record;
using Norvia.Erp.Application.IntegrationEvents;
using Norvia.Erp.Domain.BillableLines;

namespace Norvia.Erp.Api.BillableLines;

public static class BillableLineEndpoints
{
    /// <summary>The app role only the Billing Logic App's managed identity holds, so nothing bypasses the integration layer.</summary>
    public const string RecordRole = "BillableLines.Record";

    public static IEndpointRouteBuilder MapBillableLines(this IEndpointRouteBuilder app)
    {
        var billableLines = app.NewVersionedApi("BillableLines")
            .MapGroup("/v{version:apiVersion}/billable-lines")
            .HasApiVersion(1);

        billableLines.MapPost("/", RecordAsync)
            .WithName("RecordBillableLine")
            .RequireAuthorization(policy => policy.RequireRole(RecordRole))
            .WithMetadata(new TrackedFlow(Flows.Billing))
            .WithSummary("Record the Billable line a TransportOrderBillable carries, as Waiting.")
            .WithDescription(
                "The body is the Canonical Model's TransportOrderBillable, version 1, and nothing else. Idempotent on the Transport Order number: "
                + "a repeat with the same content answers 200 with the original line and repeat true; the first recording answers 201. "
                + "Publishes nothing. Refusals carry a stable code: ShipperNotFound (422), BillableLineConflict (409, the Transport Order is "
                + "already recorded with different content), FreightChargeWithoutChargeLines and FreightChargeTotalMismatch (400).");

        return app;
    }

    internal static async Task<Results<Ok<RecordBillableLineResult>, Created<RecordBillableLineResult>, ProblemHttpResult, ValidationProblem>> RecordAsync(
        TransportOrderBillableRequest? request,
        IConflictRetrier retrier,
        CancellationToken cancellationToken)
    {
        var (data, errors) = TransportOrderBillableValidator.Validate(request);
        if (data is null)
        {
            return TypedResults.ValidationProblem(errors);
        }

        var result = await retrier.RunAsync(mediator => mediator.Send(new RecordBillableLineCommand(data), cancellationToken).AsTask());
        if (!result.IsSuccess)
        {
            return Refused(result, data.BillingCorrelationId);
        }

        return result.Value.Repeat
            ? TypedResults.Ok(result.Value)
            : TypedResults.Created($"/v1/billable-lines/{data.TransportOrderNumber}", result.Value);
    }

    private static ProblemHttpResult Refused(Ardalis.Result.Result<RecordBillableLineResult> result, string correlationId) =>
        RefusalProblem.Of<BillableLineRefusalCode, RecordBillableLineResult>(
            result,
            "The Billable line can't be recorded.",
            correlationId,
            code => code switch
            {
                BillableLineRefusalCode.ShipperNotFound => StatusCodes.Status422UnprocessableEntity,
                BillableLineRefusalCode.BillableLineConflict => StatusCodes.Status409Conflict,
                _ => StatusCodes.Status400BadRequest,
            });
}
