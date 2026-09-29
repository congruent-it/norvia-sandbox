using Ardalis.Result;
using Mediator;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Norvia.Tms.Api.Shipments;
using Norvia.Tms.Api.Tracking;
using Norvia.Tms.Api.TransportOrders;
using Norvia.Tms.Application.CarrierBookings.Answer;
using Norvia.Tms.Application.CarrierBookings.Report;
using Norvia.Tms.Application.CarrierBookings.Withdraw;
using Norvia.Tms.Application.IntegrationEvents;
using Norvia.Tms.Domain.CarrierBookings;
using Norvia.Tms.Domain.Shipments;
using Norvia.Tms.Domain.TransportOrders;

namespace Norvia.Tms.Api.CarrierBookings;

public static class CarrierBookingEndpoints
{
    /// <summary>
    /// The app role only the Carrier-answer Logic App's managed identity holds. Added now, even though the Logic App
    /// that will hold it is a later ticket, so nothing bypasses the integration layer once it exists.
    /// </summary>
    public const string AnswerRole = "CarrierBookings.Answer";

    /// <summary>The app role only the P4 booking Logic App's managed identity holds, for its sent and not-delivered reports.</summary>
    public const string ReportRole = "CarrierBookings.Report";

    public static IEndpointRouteBuilder MapCarrierBookings(this IEndpointRouteBuilder app)
    {
        var carrierBookings = app.NewVersionedApi("CarrierBookings")
            .MapGroup("/v{version:apiVersion}/carrier-bookings")
            .HasApiVersion(1);

        carrierBookings.MapPost("/{carrierBookingNumber}/answer", AnswerAsync)
            .WithName("AnswerCarrierBooking")
            .RequireAuthorization(policy => policy.RequireRole(AnswerRole))
            .WithMetadata(new TrackedFlow(Flows.CarrierBookingAnswer))
            .WithSummary("Record the Carrier's confirmation or rejection of a Requested Carrier booking.")
            .WithDescription(
                "A confirmation carries the Carrier's own booking reference (required) and, optionally, a planned collection time: the booking "
                + "becomes Confirmed and the Shipment Booked. A rejection carries a reason code (required, one of NoCapacity, LaneNotServed, "
                + "WindowNotFeasible, GoodsNotAccepted or Other) and, for Other, a reason text (required): the booking becomes Rejected and the "
                + "Shipment goes back to Planned. Either way, the matching event is published on the Shipment events topic in the same "
                + "transaction; the Shipment's move publishes no event of its own. An answer sent again with the same outcome and content is "
                + "answered as before, flagged as a repeat, without recording anything again or publishing a new event. An answer is refused, "
                + "changing and publishing nothing, with a problem whose correlationId names the Message trail the refusal is traced on: "
                + "BookingNotFound (404, with that code) or BookingNotOpen (409, with that code) for a conflicting answer to a booking that "
                + "already has one, or the wrong Carrier answering (403, no code). A Carrier made inactive since the booking was sent can still "
                + "answer it.");

        carrierBookings.MapPost("/{carrierBookingNumber}/report-sent", ReportSentAsync)
            .WithName("ReportCarrierBookingSent")
            .RequireAuthorization(policy => policy.RequireRole(ReportRole))
            .WithMetadata(new TrackedFlow(Flows.CarrierBookingReport))
            .WithSummary("Report that a Requested Carrier booking's request reached the Carrier.")
            .WithDescription(
                "Records the sent time on the booking and publishes nothing. A report for a booking that is no longer Requested (for example, "
                + "withdrawn while the report was in flight) is ignored and logged: it changes nothing. BookingNotFound (404, with that code) for an "
                + "unknown Carrier booking number.");

        carrierBookings.MapPost("/{carrierBookingNumber}/report-not-delivered", ReportNotDeliveredAsync)
            .WithName("ReportCarrierBookingNotDelivered")
            .RequireAuthorization(policy => policy.RequireRole(ReportRole))
            .WithMetadata(new TrackedFlow(Flows.CarrierBookingReport))
            .WithSummary("Report that a Requested Carrier booking's request never reached the Carrier, with its technical cause.")
            .WithDescription(
                "Moves the booking to NotDelivered and the Shipment back to Planned, and publishes CarrierBookingNotDelivered, in one transaction; "
                + "the Shipment's move publishes no event of its own. A report for a booking that is no longer Requested (for example, withdrawn "
                + "while the report was in flight) is ignored and logged: it changes and publishes nothing. BookingNotFound (404, with that code) for "
                + "an unknown Carrier booking number.");

        // The Planner withdraws from the TMS UI, with the same authorization as P3's other planning commands.
        carrierBookings.MapPost("/{carrierBookingNumber}/withdraw", WithdrawAsync)
            .WithName("WithdrawCarrierBooking")
            .RequireAuthorization(policy => policy.RequireRole(TransportOrderEndpoints.ManageRole))
            .WithMetadata(new TrackedFlow(Flows.TransportPlanning))
            .WithSummary("Withdraw a Requested Carrier booking before the Carrier answers.")
            .WithDescription(
                "Moves the booking to Withdrawn and the Shipment back to Planned, and publishes CarrierBookingWithdrawn, in one transaction; the "
                + "Shipment's move publishes no event of its own. A later answer for it is refused with BookingNotOpen, and a later sent or "
                + "not-delivered report for it is ignored. A withdrawal sent again under its Idempotency-Key is answered as before, flagged as a "
                + "repeat, without withdrawing again. A withdrawal that breaks the rule is refused, changing and publishing nothing, with a problem "
                + "whose correlationId names the Message trail the refusal is traced on: BookingNotFound (404, with that code) or BookingNotOpen "
                + "(409, with that code) for a booking that already has a different outcome.");

        return app;
    }

    private static async Task<Results<Ok<CarrierBookingAnswerResult>, ProblemHttpResult, ValidationProblem>> AnswerAsync(
        string carrierBookingNumber,
        [FromHeader(Name = TransportOrderEndpoints.CorrelationIdHeader)] string? correlationId,
        CarrierBookingAnswerRequest answer,
        IMediator mediator,
        CancellationToken cancellationToken)
    {
        var errors = Check(correlationId, answer);
        if (errors.Count > 0)
        {
            return TypedResults.ValidationProblem(errors);
        }

        Result<CarrierBookingAnswerResult> result = answer.Outcome == CarrierBookingAnswerOutcome.Confirmed
            ? await mediator.Send(
                new ConfirmCarrierBookingCommand(
                    carrierBookingNumber, answer.CarrierId, correlationId!, answer.CarrierBookingReference!, answer.PlannedCollectionTime),
                cancellationToken)
            : await mediator.Send(
                new RejectCarrierBookingCommand(carrierBookingNumber, answer.CarrierId, correlationId!, answer.ReasonCode!.Value, answer.ReasonText),
                cancellationToken);

        return result.IsSuccess
            ? TypedResults.Ok(result.Value)
            : Refused(result, "The Carrier's answer can't be recorded.", correlationId!);
    }

    private static async Task<Results<Ok<CarrierBookingReportResult>, ProblemHttpResult, ValidationProblem>> ReportSentAsync(
        string carrierBookingNumber,
        [FromHeader(Name = TransportOrderEndpoints.CorrelationIdHeader)] string? correlationId,
        IMediator mediator,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(correlationId))
        {
            return TypedResults.ValidationProblem(
                new Dictionary<string, string[]> { [TransportOrderEndpoints.CorrelationIdHeader] = [$"Reporting a Carrier booking needs an {TransportOrderEndpoints.CorrelationIdHeader}."] });
        }

        var result = await mediator.Send(new ReportCarrierBookingSentCommand(carrierBookingNumber, correlationId), cancellationToken);
        return result.IsSuccess ? TypedResults.Ok(result.Value) : Refused(result, "The report can't be recorded.", correlationId);
    }

    private static async Task<Results<Ok<CarrierBookingReportResult>, ProblemHttpResult, ValidationProblem>> ReportNotDeliveredAsync(
        string carrierBookingNumber,
        [FromHeader(Name = TransportOrderEndpoints.CorrelationIdHeader)] string? correlationId,
        CarrierBookingNotDeliveredRequest report,
        IMediator mediator,
        CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(correlationId))
        {
            errors[TransportOrderEndpoints.CorrelationIdHeader] = [$"Reporting a Carrier booking needs an {TransportOrderEndpoints.CorrelationIdHeader}."];
        }

        if (report.StatusCode is null)
        {
            errors[nameof(CarrierBookingNotDeliveredRequest.StatusCode)] = ["A not-delivered report needs the technical cause's status code."];
        }

        if (string.IsNullOrWhiteSpace(report.Message))
        {
            errors[nameof(CarrierBookingNotDeliveredRequest.Message)] = ["A not-delivered report needs the technical cause's message."];
        }

        if (errors.Count > 0)
        {
            return TypedResults.ValidationProblem(errors);
        }

        var result = await mediator.Send(
            new ReportCarrierBookingNotDeliveredCommand(carrierBookingNumber, correlationId!, report.StatusCode!.Value, report.Message!), cancellationToken);
        return result.IsSuccess ? TypedResults.Ok(result.Value) : Refused(result, "The report can't be recorded.", correlationId!);
    }

    private static async Task<Results<Ok<CarrierBookingWithdrawResult>, ProblemHttpResult, ValidationProblem>> WithdrawAsync(
        string carrierBookingNumber,
        [FromHeader(Name = TransportOrderEndpoints.IdempotencyKeyHeader)] string? idempotencyKey,
        IMediator mediator,
        CancellationToken cancellationToken)
    {
        var errors = AssignmentCalls.Check(PlanningAction.Withdrawal, idempotencyKey);
        if (errors.Count > 0)
        {
            return TypedResults.ValidationProblem(errors);
        }

        // The action starts in the TMS UI, not at API Management, so the TMS starts its Message trail.
        var correlationId = CorrelationId.New().Value;
        var result = await mediator.Send(new WithdrawCarrierBookingCommand(carrierBookingNumber, idempotencyKey!, correlationId), cancellationToken);
        return result.IsSuccess ? TypedResults.Ok(result.Value) : Refused(result, "The Carrier booking can't be withdrawn.", correlationId);
    }

    /// <summary>
    /// The call's errors: an <see cref="TransportOrderEndpoints.CorrelationIdHeader"/>, a confirmation's Carrier booking
    /// reference, a rejection's reason code and, for <c>Other</c>, its reason text. Whether the reason code is one of
    /// the known ones is enforced by its type: an unknown one fails to parse as JSON, which is a client error too.
    /// </summary>
    private static Dictionary<string, string[]> Check(string? correlationId, CarrierBookingAnswerRequest answer)
    {
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(correlationId))
        {
            errors[TransportOrderEndpoints.CorrelationIdHeader] =
                [$"Recording a Carrier's answer needs an {TransportOrderEndpoints.CorrelationIdHeader}."];
        }

        if (answer.Outcome == CarrierBookingAnswerOutcome.Confirmed && string.IsNullOrWhiteSpace(answer.CarrierBookingReference))
        {
            errors[nameof(CarrierBookingAnswerRequest.CarrierBookingReference)] = ["A confirmation needs the Carrier's own booking reference."];
        }
        else if (answer.Outcome == CarrierBookingAnswerOutcome.Rejected && answer.ReasonCode is null)
        {
            errors[nameof(CarrierBookingAnswerRequest.ReasonCode)] = ["A rejection needs a reason code."];
        }
        else if (answer.Outcome == CarrierBookingAnswerOutcome.Rejected
            && answer.ReasonCode == CarrierBookingRejectionReason.Other
            && string.IsNullOrWhiteSpace(answer.ReasonText))
        {
            errors[nameof(CarrierBookingAnswerRequest.ReasonText)] = ["A rejection with reason Other needs its reason text."];
        }

        return errors;
    }

    /// <summary>
    /// The problem for a refused call, with a status that says which rule it broke, the refusal's own code where it has
    /// one (the wrong Carrier answering has none), and the call's own correlation ID, which the refusal is traced on, so
    /// the Integration console can join its Message trail.
    /// </summary>
    private static ProblemHttpResult Refused<T>(Result<T> result, string title, string correlationId)
    {
        var refusal = result.ValidationErrors.First();
        var extensions = new Dictionary<string, object?> { ["correlationId"] = correlationId };
        if (!string.IsNullOrEmpty(refusal.ErrorCode))
        {
            extensions["code"] = refusal.ErrorCode;
        }

        return TypedResults.Problem(
            statusCode: string.IsNullOrEmpty(refusal.ErrorCode) ? StatusCodes.Status403Forbidden : StatusCodeOf(Enum.Parse<CarrierBookingRefusalCode>(refusal.ErrorCode)),
            title: title,
            detail: refusal.ErrorMessage,
            extensions: extensions);
    }

    private static int StatusCodeOf(CarrierBookingRefusalCode code) => code switch
    {
        CarrierBookingRefusalCode.BookingNotFound => StatusCodes.Status404NotFound,
        _ => StatusCodes.Status409Conflict, // BookingNotOpen
    };
}
