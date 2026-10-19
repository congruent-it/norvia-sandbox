using Mediator;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Norvia.Erp.Api.Tracking;
using Norvia.Erp.Application;
using Norvia.Erp.Application.CreditNotes.ReportNotDelivered;
using Norvia.Erp.Application.CreditNotes.ReportSent;
using Norvia.Erp.Application.IntegrationEvents;
using Norvia.Erp.Domain.CreditNotes;

namespace Norvia.Erp.Api.ShipperInvoices;

/// <summary>
/// The Shipper invoice Logic App's reports on the Credit notes it sent (E1), the same as for an invoice and with the same role.
/// </summary>
public static class CreditNoteEndpoints
{
    public const string CorrelationIdHeader = TrackingScopeMiddleware.CorrelationIdHeader;

    public static IEndpointRouteBuilder MapCreditNotes(this IEndpointRouteBuilder app)
    {
        var creditNotes = app.NewVersionedApi("CreditNotes")
            .MapGroup("/v{version:apiVersion}/credit-notes")
            .HasApiVersion(1);

        creditNotes.MapPost("/{creditNoteNumber}/report-sent", ReportSentAsync)
            .WithName("ReportCreditNoteSent")
            .RequireAuthorization(policy => policy.RequireRole(ShipperInvoiceEndpoints.ReportRole))
            .WithMetadata(new TrackedFlow(Flows.Invoicing))
            .WithSummary("Report that the Shipper's invoice endpoint acknowledged a Credit note.")
            .WithDescription(
                "Moves the Credit note from Issued to Sent, records the time and publishes nothing. A report for a Credit note that was reported "
                + "already is ignored and logged: it answers 200 with ignored true and changes nothing. The X-Correlation-ID is the correlation ID "
                + "the ERP created for the Credit note, which CreditNoteIssued carried. CreditNoteNotFound (404, with that code) for an unknown number.");

        creditNotes.MapPost("/{creditNoteNumber}/report-not-delivered", ReportNotDeliveredAsync)
            .WithName("ReportCreditNoteNotDelivered")
            .RequireAuthorization(policy => policy.RequireRole(ShipperInvoiceEndpoints.ReportRole))
            .WithMetadata(new TrackedFlow(Flows.Invoicing))
            .WithSummary("Report that a Credit note never reached the Shipper, with its technical cause.")
            .WithDescription(
                "Moves the Credit note from Issued to Not delivered with the technical cause and publishes ShipperInvoiceNotDelivered with document type "
                + "CREDIT_NOTE, in one transaction. A report for a Credit note that isn't Issued any more (reported already, or sent again since) is ignored "
                + "and logged: it answers 200 with ignored true and changes and publishes nothing. The X-Correlation-ID is the correlation ID the ERP "
                + "created for the Credit note. CreditNoteNotFound (404, with that code) for an unknown number.");

        return app;
    }

    internal static async Task<Results<Ok<CreditNoteReportResult>, ProblemHttpResult, ValidationProblem>> ReportSentAsync(
        string creditNoteNumber,
        [FromHeader(Name = CorrelationIdHeader)] string? correlationId,
        IConflictRetrier retrier,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(correlationId))
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                [CorrelationIdHeader] = [$"Reporting a Credit note needs an {CorrelationIdHeader}."],
            });
        }

        var result = await retrier.RunAsync(mediator => mediator.Send(new ReportCreditNoteSentCommand(creditNoteNumber, correlationId), cancellationToken).AsTask());
        return result.IsSuccess
            ? TypedResults.Ok(result.Value)
            : RefusalProblem.Of<CreditNoteRefusalCode, CreditNoteReportResult>(
                result, "The report can't be recorded.", correlationId, _ => StatusCodes.Status404NotFound);
    }

    internal static async Task<Results<Ok<CreditNoteReportResult>, ProblemHttpResult, ValidationProblem>> ReportNotDeliveredAsync(
        string creditNoteNumber,
        [FromHeader(Name = CorrelationIdHeader)] string? correlationId,
        ShipperInvoiceNotDeliveredRequest report,
        IConflictRetrier retrier,
        CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(correlationId))
        {
            errors[CorrelationIdHeader] = [$"Reporting a Credit note needs an {CorrelationIdHeader}."];
        }

        if (string.IsNullOrWhiteSpace(report.Message))
        {
            errors[nameof(ShipperInvoiceNotDeliveredRequest.Message)] = ["A not-delivered report needs the technical cause's message."];
        }

        if (errors.Count > 0 || correlationId is null || report.Message is not { } message)
        {
            return TypedResults.ValidationProblem(errors);
        }

        var result = await retrier.RunAsync(mediator => mediator.Send(new ReportCreditNoteNotDeliveredCommand(creditNoteNumber, correlationId, report.StatusCode, message), cancellationToken).AsTask());
        return result.IsSuccess
            ? TypedResults.Ok(result.Value)
            : RefusalProblem.Of<CreditNoteRefusalCode, CreditNoteReportResult>(
                result, "The report can't be recorded.", correlationId, _ => StatusCodes.Status404NotFound);
    }
}
