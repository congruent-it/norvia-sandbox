using Mediator;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Norvia.Erp.Api.Tracking;
using Norvia.Erp.Application;
using Norvia.Erp.Application.CreditNotes;
using Norvia.Erp.Application.CreditNotes.Issue;
using Norvia.Erp.Application.IntegrationEvents;
using Norvia.Erp.Application.ShipperInvoices.Issue;
using Norvia.Erp.Application.ShipperInvoices.ReportNotDelivered;
using Norvia.Erp.Application.ShipperInvoices.ReportSent;
using Norvia.Erp.Domain.CreditNotes;
using Norvia.Erp.Domain.ShipperInvoices;

namespace Norvia.Erp.Api.ShipperInvoices;

public static class ShipperInvoiceEndpoints
{
    /// <summary>The app role only the Shipper invoice Logic App's managed identity holds, for its reports on what it sent.</summary>
    public const string ReportRole = "ShipperInvoices.Report";

    /// <summary>The app role only the Simulator's identity holds, for the Finance actions it plays: Invoice now and a Credit note.</summary>
    public const string ManageRole = "ShipperInvoices.Manage";

    public const string CorrelationIdHeader = TrackingScopeMiddleware.CorrelationIdHeader;

    public static IEndpointRouteBuilder MapShipperInvoices(this IEndpointRouteBuilder app)
    {
        var invoices = app.NewVersionedApi("ShipperInvoices")
            .MapGroup("/v{version:apiVersion}/shipper-invoices")
            .HasApiVersion(1);

        invoices.MapPost("/", InvoiceNowAsync)
            .WithName("InvoiceNow")
            .RequireAuthorization(policy => policy.RequireRole(ManageRole))
            .WithMetadata(new TrackedFlow(Flows.Invoicing))
            .WithSummary("Invoice now: run the Invoice run for one Shipper over all its Waiting lines.")
            .WithDescription(
                "What Finance's Invoice now button does in the ERP UI, through the same command. Issues one Shipper invoice, sets the lines to Invoiced and "
                + "publishes ShipperInvoiceIssued, in one transaction. Answers 200 with the invoice, or with invoice null when the Shipper has no Waiting lines. "
                + "ShipperNotFound (404, with that code) for an unknown Shipper.");

        invoices.MapPost("/{invoiceNumber}/credit-notes", IssueCreditNoteAsync)
            .WithName("IssueCreditNote")
            .RequireAuthorization(policy => policy.RequireRole(ManageRole))
            .WithMetadata(new TrackedFlow(Flows.Invoicing))
            .WithSummary("Issue a Credit note on one line of a Shipper invoice, in full or in part.")
            .WithDescription(
                "What Finance's Credit note form does in the ERP UI, through the same command. The amount is without VAT. Issues the Credit note and publishes "
                + "CreditNoteIssued, in one transaction, and answers 201 with it. Refusals carry the stable code of the Credit note rule broken: "
                + "ShipperInvoiceNotFound and InvoiceLineNotFound (404), CreditNoteReasonRequired, CreditNoteReasonTooLong and CreditNoteAmountInvalid (400), "
                + "CreditNoteAboveLineTotal (422).");

        invoices.MapPost("/{invoiceNumber}/report-sent", ReportSentAsync)
            .WithName("ReportShipperInvoiceSent")
            .RequireAuthorization(policy => policy.RequireRole(ReportRole))
            .WithMetadata(new TrackedFlow(Flows.Invoicing))
            .WithSummary("Report that the Shipper's invoice endpoint acknowledged a Shipper invoice.")
            .WithDescription(
                "Moves the invoice from Issued to Sent, records the time and publishes nothing. A report for an invoice that was reported "
                + "already is ignored and logged: it answers 200 with ignored true and changes nothing. The X-Correlation-ID is the correlation ID "
                + "the ERP created for the invoice, which ShipperInvoiceIssued carried. ShipperInvoiceNotFound (404, with that code) for an unknown invoice number.");

        invoices.MapPost("/{invoiceNumber}/report-not-delivered", ReportNotDeliveredAsync)
            .WithName("ReportShipperInvoiceNotDelivered")
            .RequireAuthorization(policy => policy.RequireRole(ReportRole))
            .WithMetadata(new TrackedFlow(Flows.Invoicing))
            .WithSummary("Report that a Shipper invoice never reached the Shipper, with its technical cause.")
            .WithDescription(
                "Moves the invoice from Issued to Not delivered with the technical cause and publishes ShipperInvoiceNotDelivered, in one transaction. "
                + "A report for an invoice that isn't Issued any more (reported already, or sent again since) is ignored and logged: it answers 200 with "
                + "ignored true and changes and publishes nothing. The X-Correlation-ID is the correlation ID the ERP created for the invoice. "
                + "ShipperInvoiceNotFound (404, with that code) for an unknown invoice number.");

        return app;
    }

    internal static async Task<Results<Ok<IssueShipperInvoiceResult>, ProblemHttpResult, ValidationProblem>> InvoiceNowAsync(
        InvoiceNowRequest? request,
        IConflictRetrier retrier,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request?.ShipperId))
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]> { [nameof(InvoiceNowRequest.ShipperId)] = ["Invoice now needs the Shipper's ID."] });
        }

        var shipperId = request.ShipperId;
        var result = await retrier.RunAsync(mediator => mediator.Send(new IssueShipperInvoiceCommand(shipperId), cancellationToken).AsTask());
        if (result.IsSuccess)
        {
            return TypedResults.Ok(result.Value);
        }

        return TypedResults.Problem(
            statusCode: StatusCodes.Status404NotFound,
            title: "Invoice now can't be run.",
            detail: string.Join(' ', result.Errors),
            extensions: new Dictionary<string, object?> { ["code"] = "ShipperNotFound" });
    }

    internal static async Task<Results<Created<CreditNoteView>, ProblemHttpResult, ValidationProblem>> IssueCreditNoteAsync(
        string invoiceNumber,
        IssueCreditNoteRequest? request,
        IConflictRetrier retrier,
        CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(request?.TransportOrderNumber))
        {
            errors[nameof(IssueCreditNoteRequest.TransportOrderNumber)] = ["A Credit note needs the Transport Order number of the invoice line."];
        }

        if (request?.AmountWithoutVat is null)
        {
            errors[nameof(IssueCreditNoteRequest.AmountWithoutVat)] = ["A Credit note needs the amount without VAT."];
        }

        if (errors.Count > 0 || request?.TransportOrderNumber is not { } transportOrderNumber || request.AmountWithoutVat is not { } amount)
        {
            return TypedResults.ValidationProblem(errors);
        }

        var result = await retrier.RunAsync(
            mediator => mediator.Send(new IssueCreditNoteCommand(invoiceNumber, transportOrderNumber, amount, request.Reason), cancellationToken).AsTask());
        return result.IsSuccess
            ? TypedResults.Created($"/v1/credit-notes/{result.Value.CreditNoteNumber}", result.Value)
            : RefusalProblem.Of<CreditNoteRefusalCode, CreditNoteView>(
                result, "The Credit note can't be issued.", Guid.NewGuid().ToString(), code => code switch
                {
                    CreditNoteRefusalCode.ShipperInvoiceNotFound or CreditNoteRefusalCode.InvoiceLineNotFound => StatusCodes.Status404NotFound,
                    CreditNoteRefusalCode.CreditNoteAboveLineTotal => StatusCodes.Status422UnprocessableEntity,
                    _ => StatusCodes.Status400BadRequest,
                });
    }

    internal static async Task<Results<Ok<ShipperInvoiceReportResult>, ProblemHttpResult, ValidationProblem>> ReportSentAsync(
        string invoiceNumber,
        [FromHeader(Name = CorrelationIdHeader)] string? correlationId,
        IConflictRetrier retrier,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(correlationId))
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                [CorrelationIdHeader] = [$"Reporting a Shipper invoice needs an {CorrelationIdHeader}."],
            });
        }

        var result = await retrier.RunAsync(mediator => mediator.Send(new ReportShipperInvoiceSentCommand(invoiceNumber, correlationId), cancellationToken).AsTask());
        return result.IsSuccess
            ? TypedResults.Ok(result.Value)
            : RefusalProblem.Of<ShipperInvoiceRefusalCode, ShipperInvoiceReportResult>(
                result, "The report can't be recorded.", correlationId, _ => StatusCodes.Status404NotFound);
    }

    internal static async Task<Results<Ok<ShipperInvoiceReportResult>, ProblemHttpResult, ValidationProblem>> ReportNotDeliveredAsync(
        string invoiceNumber,
        [FromHeader(Name = CorrelationIdHeader)] string? correlationId,
        ShipperInvoiceNotDeliveredRequest report,
        IConflictRetrier retrier,
        CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(correlationId))
        {
            errors[CorrelationIdHeader] = [$"Reporting a Shipper invoice needs an {CorrelationIdHeader}."];
        }

        if (string.IsNullOrWhiteSpace(report.Message))
        {
            errors[nameof(ShipperInvoiceNotDeliveredRequest.Message)] = ["A not-delivered report needs the technical cause's message."];
        }

        if (errors.Count > 0 || correlationId is null || report.Message is not { } message)
        {
            return TypedResults.ValidationProblem(errors);
        }

        var result = await retrier.RunAsync(mediator => mediator.Send(new ReportShipperInvoiceNotDeliveredCommand(invoiceNumber, correlationId, report.StatusCode, message), cancellationToken).AsTask());
        return result.IsSuccess
            ? TypedResults.Ok(result.Value)
            : RefusalProblem.Of<ShipperInvoiceRefusalCode, ShipperInvoiceReportResult>(
                result, "The report can't be recorded.", correlationId, _ => StatusCodes.Status404NotFound);
    }
}

/// <summary>The Shipper invoice Logic App's Not delivered report: the Shipper endpoint's status code, if it answered, and the cause.</summary>
/// <param name="StatusCode">The HTTP status code, or null when the failure had none, such as a timeout.</param>
/// <param name="Message">The technical cause in plain language.</param>
public sealed record ShipperInvoiceNotDeliveredRequest(int? StatusCode, string? Message);

/// <summary>Invoice now for one Shipper.</summary>
public sealed record InvoiceNowRequest(string? ShipperId);

/// <summary>A Credit note on the line of the invoice that holds this Transport Order, for an amount without VAT and a reason.</summary>
public sealed record IssueCreditNoteRequest(string? TransportOrderNumber, decimal? AmountWithoutVat, string? Reason);
