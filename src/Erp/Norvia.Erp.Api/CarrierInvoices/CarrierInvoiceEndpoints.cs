using Mediator;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Norvia.Agents;
using Norvia.Erp.Api.Tracking;
using Norvia.Erp.Application;
using Norvia.Erp.Application.CarrierInvoices;
using Norvia.Erp.Application.CarrierInvoices.Decide;
using Norvia.Erp.Application.CarrierInvoices.Receive;
using Norvia.Erp.Application.CarrierInvoices.ReportNotDelivered;
using Norvia.Erp.Application.CarrierInvoices.ReportTold;
using Norvia.Erp.Application.IntegrationEvents;
using Norvia.Erp.Domain.CarrierInvoices;
using Norvia.Erp.Domain.Carriers;

namespace Norvia.Erp.Api.CarrierInvoices;

public static class CarrierInvoiceEndpoints
{
    /// <summary>The app role only the Carrier invoice Logic App's managed identity holds, so nothing bypasses the integration layer.</summary>
    public const string ReceiveRole = "CarrierInvoices.Receive";

    /// <summary>The app role only the Carrier invoice outcome Logic App's managed identity holds, for its reports on what it told the Carrier.</summary>
    public const string ReportRole = "CarrierInvoices.Report";

    /// <summary>The app role only the Simulator's identity holds, for the Finance decisions on Carrier invoice lines it plays.</summary>
    public const string ManageRole = "CarrierInvoices.Manage";

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

        carrierInvoices.MapPost("/{carrierId}/{invoiceNumber}/lines/{bookingReference}/accept", AcceptLineAsync)
            .WithName("AcceptCarrierInvoiceLine")
            .RequireAuthorization(policy => policy.RequireRole(ManageRole))
            .WithMetadata(new TrackedFlow(Flows.CarrierInvoices))
            .WithSummary("Accept a Carrier invoice line that waits for Finance, at the invoiced amount or an adjusted one.")
            .WithDescription(
                "What Finance's Accept button does in the ERP UI, through the same command. The optional adjusted amount needs a remark. The line's Carrier "
                + "cost becomes Invoiced; when it is the last line to decide, the invoice becomes Approved and CarrierInvoiceApproved is published, once, in "
                + "the same transaction. Answers 200 with the invoice. Refusals carry the stable code: CarrierInvoiceNotFound and CarrierInvoiceLineNotFound "
                + "(404), CarrierInvoiceLineAlreadyDecided (409), CarrierInvoiceLineHasNoOpenCarrierCost (422), CarrierInvoiceAdjustedAmountNotAboveZero and "
                + "CarrierInvoiceAdjustedAmountNeedsRemark (400). The optional agentName and agentRunId, together, name the Agent whose Suggestion Finance acted on, for the trace.");

        carrierInvoices.MapPost("/{carrierId}/{invoiceNumber}/lines/{bookingReference}/reject", RejectLineAsync)
            .WithName("RejectCarrierInvoiceLine")
            .RequireAuthorization(policy => policy.RequireRole(ManageRole))
            .WithMetadata(new TrackedFlow(Flows.CarrierInvoices))
            .WithSummary("Reject a Carrier invoice line that waits for Finance, with a reason.")
            .WithDescription(
                "What Finance's Reject button does in the ERP UI, through the same command. The line's Carrier cost, if any, stays as it was; when it is the "
                + "last line to decide, the invoice becomes Approved and CarrierInvoiceApproved is published, once, in the same transaction. Answers 200 with "
                + "the invoice. Refusals carry the stable code: CarrierInvoiceNotFound and CarrierInvoiceLineNotFound (404), CarrierInvoiceLineAlreadyDecided "
                + "(409), CarrierInvoiceRejectionNeedsReason (400).");

        return app;
    }

    internal static async Task<Results<Ok<CarrierInvoiceView>, ProblemHttpResult, ValidationProblem>> AcceptLineAsync(
        string carrierId,
        string invoiceNumber,
        string bookingReference,
        AcceptCarrierInvoiceLineRequest? request,
        IConflictRetrier retrier,
        CancellationToken cancellationToken)
    {
        if (ActingOn(request?.AgentName, request?.AgentRunId, out var actingOn) is { } invalid)
        {
            return invalid;
        }

        var result = await retrier.RunAsync(
            mediator => mediator.Send(
                new AcceptCarrierInvoiceLineCommand(new CarrierId(carrierId), invoiceNumber, bookingReference, request?.AdjustedAmount, request?.Remark, actingOn), cancellationToken).AsTask());
        return DecisionAnswer(result, "The Carrier invoice line can't be accepted.");
    }

    internal static async Task<Results<Ok<CarrierInvoiceView>, ProblemHttpResult, ValidationProblem>> RejectLineAsync(
        string carrierId,
        string invoiceNumber,
        string bookingReference,
        RejectCarrierInvoiceLineRequest? request,
        IConflictRetrier retrier,
        CancellationToken cancellationToken)
    {
        if (ActingOn(request?.AgentName, request?.AgentRunId, out var actingOn) is { } invalid)
        {
            return invalid;
        }

        var result = await retrier.RunAsync(
            mediator => mediator.Send(new RejectCarrierInvoiceLineCommand(new CarrierId(carrierId), invoiceNumber, bookingReference, request?.Reason, actingOn), cancellationToken).AsTask());
        return DecisionAnswer(result, "The Carrier invoice line can't be rejected.");
    }

    // The Agent's name and run come together or not at all, and are short: they are only for the trace (ADR 0040).
    private static ValidationProblem? ActingOn(string? agentName, string? agentRunId, out AgentRun? actingOn)
    {
        actingOn = null;
        if (string.IsNullOrWhiteSpace(agentName) != string.IsNullOrWhiteSpace(agentRunId))
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]> { [nameof(AcceptCarrierInvoiceLineRequest.AgentRunId)] = ["The Agent and its run ID come together."] });
        }

        if (agentName?.Length > 100 || agentRunId?.Length > 200)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]> { [nameof(AcceptCarrierInvoiceLineRequest.AgentRunId)] = ["The Agent's name is at most 100 characters and its run ID 200."] });
        }

        actingOn = string.IsNullOrWhiteSpace(agentName) ? null : new AgentRun(agentName.Trim(), agentRunId!.Trim());
        return null;
    }

    private static Results<Ok<CarrierInvoiceView>, ProblemHttpResult, ValidationProblem> DecisionAnswer(Ardalis.Result.Result<CarrierInvoiceView> result, string title) =>
        result.IsSuccess
            ? TypedResults.Ok(result.Value)
            : RefusalProblem.Of<CarrierInvoiceRefusalCode, CarrierInvoiceView>(
                result, title, Guid.NewGuid().ToString(), code => code switch
                {
                    CarrierInvoiceRefusalCode.CarrierInvoiceNotFound or CarrierInvoiceRefusalCode.CarrierInvoiceLineNotFound => StatusCodes.Status404NotFound,
                    CarrierInvoiceRefusalCode.CarrierInvoiceLineAlreadyDecided => StatusCodes.Status409Conflict,
                    CarrierInvoiceRefusalCode.CarrierInvoiceLineHasNoOpenCarrierCost => StatusCodes.Status422UnprocessableEntity,
                    _ => StatusCodes.Status400BadRequest,
                });

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

/// <summary>Finance accepting a Carrier invoice line: at the invoiced amount, or at an adjusted amount with a remark.</summary>
/// <param name="AgentName">Optional, with <paramref name="AgentRunId"/>: the Agent whose Suggestion Finance acted on, which the decision's trace then names.</param>
/// <param name="AgentRunId">Optional, with <paramref name="AgentName"/>: that Agent's run.</param>
public sealed record AcceptCarrierInvoiceLineRequest(decimal? AdjustedAmount, string? Remark, string? AgentName = null, string? AgentRunId = null);

/// <summary>Finance rejecting a Carrier invoice line, with a reason.</summary>
/// <param name="AgentName">Optional, with <paramref name="AgentRunId"/>: the Agent whose Suggestion Finance acted on, which the decision's trace then names.</param>
/// <param name="AgentRunId">Optional, with <paramref name="AgentName"/>: that Agent's run.</param>
public sealed record RejectCarrierInvoiceLineRequest(string? Reason, string? AgentName = null, string? AgentRunId = null);
