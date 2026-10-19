using Mediator;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Norvia.Erp.Api.Tracking;
using Norvia.Erp.Application;
using Norvia.Erp.Application.IntegrationEvents;
using Norvia.Erp.Application.ShipperInvoices.ReportNotDelivered;
using Norvia.Erp.Application.ShipperInvoices.ReportSent;
using Norvia.Erp.Domain.ShipperInvoices;

namespace Norvia.Erp.Api.ShipperInvoices;

public static class ShipperInvoiceEndpoints
{
    /// <summary>The app role only the Shipper invoice Logic App's managed identity holds, for its reports on what it sent.</summary>
    public const string ReportRole = "ShipperInvoices.Report";

    public const string CorrelationIdHeader = TrackingScopeMiddleware.CorrelationIdHeader;

    public static IEndpointRouteBuilder MapShipperInvoices(this IEndpointRouteBuilder app)
    {
        var invoices = app.NewVersionedApi("ShipperInvoices")
            .MapGroup("/v{version:apiVersion}/shipper-invoices")
            .HasApiVersion(1);

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
