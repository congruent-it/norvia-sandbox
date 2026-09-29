using Ardalis.Result;
using Mediator;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Norvia.Tms.Api.Tracking;
using Norvia.Tms.Api.TransportOrders;
using Norvia.Tms.Application.CarrierBookings.Answer;
using Norvia.Tms.Application.IntegrationEvents;
using Norvia.Tms.Domain.CarrierBookings;

namespace Norvia.Tms.Api.CarrierBookings;

public static class CarrierBookingEndpoints
{
    /// <summary>
    /// The app role only the Carrier-answer Logic App's managed identity holds. Added now, even though the Logic App
    /// that will hold it is a later ticket, so nothing bypasses the integration layer once it exists.
    /// </summary>
    public const string AnswerRole = "CarrierBookings.Answer";

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

        return result.IsSuccess ? TypedResults.Ok(result.Value) : Refused(result, correlationId!);
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
    /// The problem for a refused answer, with a status that says which rule it broke, the refusal's own code where it has
    /// one (the wrong Carrier answering has none), and the call's own correlation ID, which the refusal is traced on, so
    /// the Integration console can join its Message trail.
    /// </summary>
    private static ProblemHttpResult Refused(Result<CarrierBookingAnswerResult> result, string correlationId)
    {
        var refusal = result.ValidationErrors.First();
        var extensions = new Dictionary<string, object?> { ["correlationId"] = correlationId };
        if (!string.IsNullOrEmpty(refusal.ErrorCode))
        {
            extensions["code"] = refusal.ErrorCode;
        }

        return TypedResults.Problem(
            statusCode: string.IsNullOrEmpty(refusal.ErrorCode) ? StatusCodes.Status403Forbidden : StatusCodeOf(Enum.Parse<CarrierBookingRefusalCode>(refusal.ErrorCode)),
            title: "The Carrier's answer can't be recorded.",
            detail: refusal.ErrorMessage,
            extensions: extensions);
    }

    private static int StatusCodeOf(CarrierBookingRefusalCode code) => code switch
    {
        CarrierBookingRefusalCode.BookingNotFound => StatusCodes.Status404NotFound,
        _ => StatusCodes.Status409Conflict, // BookingNotOpen
    };
}
