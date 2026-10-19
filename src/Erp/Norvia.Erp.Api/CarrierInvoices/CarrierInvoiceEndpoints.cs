using Mediator;
using Microsoft.AspNetCore.Http.HttpResults;
using Norvia.Erp.Api.Tracking;
using Norvia.Erp.Application;
using Norvia.Erp.Application.CarrierInvoices.Receive;
using Norvia.Erp.Application.IntegrationEvents;
using Norvia.Erp.Domain.CarrierInvoices;

namespace Norvia.Erp.Api.CarrierInvoices;

public static class CarrierInvoiceEndpoints
{
    /// <summary>The app role only the Carrier invoice Logic App's managed identity holds, so nothing bypasses the integration layer.</summary>
    public const string ReceiveRole = "CarrierInvoices.Receive";

    public static IEndpointRouteBuilder MapCarrierInvoices(this IEndpointRouteBuilder app)
    {
        var carrierInvoices = app.NewVersionedApi("CarrierInvoices")
            .MapGroup("/v{version:apiVersion}/carrier-invoices")
            .HasApiVersion(1);

        carrierInvoices.MapPost("/", ReceiveAsync)
            .WithName("ReceiveCarrierInvoice")
            .RequireAuthorization(policy => policy.RequireRole(ReceiveRole))
            .WithMetadata(new TrackedFlow(Flows.CarrierInvoices))
            .WithSummary("Receive a Carrier invoice and match each of its lines against the Carrier cost.")
            .WithDescription(
                "The body is the Canonical Model's Carrier invoice, version 1, and nothing else; its carrierId is the Carrier API Management took "
                + "from the token. The invoice is recorded and every line matched in one transaction: Matched (accepted at once, its Carrier cost "
                + "becomes Invoiced), Price mismatch, Unknown booking or Duplicate (both wait for Finance). An invoice whose lines all match is "
                + "Approved and publishes CarrierInvoiceApproved through the outbox. Idempotent on the Carrier and its invoice number: a repeat "
                + "with the same content answers 200 with the original and repeat true, publishing nothing; the first receipt answers 201. "
                + "Refusals carry a stable code: CarrierNotFound (422), InvoiceNumberUsed (409, the number was used for different content), "
                + "CarrierInvoiceWithoutLines, CarrierInvoiceLineWithoutReference, CarrierInvoiceLineAmountNotAboveZero and "
                + "CarrierInvoiceReferenceRepeated (400).");

        return app;
    }

    internal static async Task<Results<Ok<ReceiveCarrierInvoiceResult>, Created<ReceiveCarrierInvoiceResult>, ProblemHttpResult, ValidationProblem>> ReceiveAsync(
        CarrierInvoiceRequest? request,
        IConflictRetrier retrier,
        CancellationToken cancellationToken)
    {
        var (data, errors) = CarrierInvoiceValidator.Validate(request);
        if (data is null)
        {
            return TypedResults.ValidationProblem(errors);
        }

        var result = await retrier.RunAsync(mediator => mediator.Send(new ReceiveCarrierInvoiceCommand(data), cancellationToken).AsTask());
        if (!result.IsSuccess)
        {
            return Refused(result, data.CorrelationId);
        }

        return result.Value.Repeat
            ? TypedResults.Ok(result.Value)
            : TypedResults.Created($"/v1/carrier-invoices/{Uri.EscapeDataString(data.CarrierId.Value)}/{Uri.EscapeDataString(data.InvoiceNumber)}", result.Value);
    }

    private static ProblemHttpResult Refused(Ardalis.Result.Result<ReceiveCarrierInvoiceResult> result, string correlationId) =>
        RefusalProblem.Of<CarrierInvoiceRefusalCode, ReceiveCarrierInvoiceResult>(
            result,
            "The Carrier invoice can't be received.",
            correlationId,
            code => code switch
            {
                CarrierInvoiceRefusalCode.CarrierNotFound => StatusCodes.Status422UnprocessableEntity,
                CarrierInvoiceRefusalCode.InvoiceNumberUsed => StatusCodes.Status409Conflict,
                _ => StatusCodes.Status400BadRequest,
            });
}
