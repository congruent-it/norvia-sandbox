using Mediator;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http.HttpResults;
using Norvia.Erp.Api.Tracking;
using Norvia.Erp.Application;
using Norvia.Erp.Application.CarrierInvoices.Receive;
using Norvia.Erp.Application.CarrierInvoices.ReportNotDelivered;
using Norvia.Erp.Application.CarrierInvoices.ReportTold;
using Norvia.Erp.Application.IntegrationEvents;
using Norvia.Erp.Domain.CarrierInvoices;

namespace Norvia.Erp.Api.CarrierInvoices;

public static class CarrierInvoiceEndpoints
{
    /// <summary>The app role only the Carrier invoice Logic App's managed identity holds, so nothing bypasses the integration layer.</summary>
    public const string ReceiveRole = "CarrierInvoices.Receive";

    /// <summary>The app role only the Carrier invoice outcome Logic App's managed identity holds, for its reports on what it told the Carrier.</summary>
    public const string ReportRole = "CarrierInvoices.Report";

    public const string CorrelationIdHeader = TrackingScopeMiddleware.CorrelationIdHeader;

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

        carrierInvoices.MapPost("/{carrierId}/{invoiceNumber}/report-told", ReportToldAsync)
            .WithName("ReportCarrierInvoiceTold")
            .RequireAuthorization(policy => policy.RequireRole(ReportRole))
            .WithMetadata(new TrackedFlow(Flows.CarrierInvoices))
            .WithSummary("Report that the Carrier's invoice outcome endpoint acknowledged the outcome of an Approved Carrier invoice.")
            .WithDescription(
                "Records the time the Carrier was told and publishes nothing. A report for an invoice whose outcome was reported already, or that isn't "
                + "Approved, is ignored and logged: it answers 200 with ignored true and changes nothing. The X-Correlation-ID is the Carrier invoice's "
                + "own correlation ID, which CarrierInvoiceApproved carried. CarrierInvoiceNotFound (404, with that code) for an unknown Carrier invoice.");

        carrierInvoices.MapPost("/{carrierId}/{invoiceNumber}/report-not-delivered", ReportNotDeliveredAsync)
            .WithName("ReportCarrierInvoiceOutcomeNotDelivered")
            .RequireAuthorization(policy => policy.RequireRole(ReportRole))
            .WithMetadata(new TrackedFlow(Flows.CarrierInvoices))
            .WithSummary("Report that the outcome of a Carrier invoice never reached the Carrier, with its technical cause.")
            .WithDescription(
                "Flags the invoice Carrier not told with the technical cause and publishes CarrierInvoiceOutcomeNotDelivered, in one transaction. A report "
                + "for an invoice whose outcome was reported already, or that isn't Approved, is ignored and logged: it answers 200 with ignored true and "
                + "changes and publishes nothing. The X-Correlation-ID is the Carrier invoice's own correlation ID. CarrierInvoiceNotFound (404, with that "
                + "code) for an unknown Carrier invoice.");

        return app;
    }

    internal static async Task<Results<Ok<CarrierInvoiceReportResult>, ProblemHttpResult, ValidationProblem>> ReportToldAsync(
        string carrierId,
        string invoiceNumber,
        [FromHeader(Name = CorrelationIdHeader)] string? correlationId,
        IConflictRetrier retrier,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(correlationId))
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                [CorrelationIdHeader] = [$"Reporting a Carrier invoice needs an {CorrelationIdHeader}."],
            });
        }

        var result = await retrier.RunAsync(mediator => mediator.Send(new ReportCarrierInvoiceToldCommand(carrierId, invoiceNumber, correlationId), cancellationToken).AsTask());
        return result.IsSuccess
            ? TypedResults.Ok(result.Value)
            : RefusalProblem.Of<CarrierInvoiceRefusalCode, CarrierInvoiceReportResult>(
                result, "The report can't be recorded.", correlationId, _ => StatusCodes.Status404NotFound);
    }

    internal static async Task<Results<Ok<CarrierInvoiceReportResult>, ProblemHttpResult, ValidationProblem>> ReportNotDeliveredAsync(
        string carrierId,
        string invoiceNumber,
        [FromHeader(Name = CorrelationIdHeader)] string? correlationId,
        CarrierInvoiceOutcomeNotDeliveredRequest report,
        IConflictRetrier retrier,
        CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(correlationId))
        {
            errors[CorrelationIdHeader] = [$"Reporting a Carrier invoice needs an {CorrelationIdHeader}."];
        }

        if (string.IsNullOrWhiteSpace(report.Message))
        {
            errors[nameof(CarrierInvoiceOutcomeNotDeliveredRequest.Message)] = ["A not-delivered report needs the technical cause's message."];
        }

        if (errors.Count > 0 || correlationId is null || report.Message is not { } message)
        {
            return TypedResults.ValidationProblem(errors);
        }

        var result = await retrier.RunAsync(
            mediator => mediator.Send(new ReportCarrierInvoiceOutcomeNotDeliveredCommand(carrierId, invoiceNumber, correlationId, report.StatusCode, message), cancellationToken).AsTask());
        return result.IsSuccess
            ? TypedResults.Ok(result.Value)
            : RefusalProblem.Of<CarrierInvoiceRefusalCode, CarrierInvoiceReportResult>(
                result, "The report can't be recorded.", correlationId, _ => StatusCodes.Status404NotFound);
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

/// <summary>The Carrier invoice outcome Logic App's Not delivered report: the Carrier endpoint's status code, if it answered, and the cause.</summary>
/// <param name="StatusCode">The HTTP status code, or null when the failure had none, such as a timeout.</param>
/// <param name="Message">The technical cause in plain language.</param>
public sealed record CarrierInvoiceOutcomeNotDeliveredRequest(int? StatusCode, string? Message);
