using Ardalis.Result;
using Mediator;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Norvia.Tms.Api.Tracking;
using Norvia.Tms.Api.TransportOrders;
using Norvia.Tms.Application.CarrierBookings.Answer;
using Norvia.Tms.Application.IntegrationEvents;

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
                + "becomes Confirmed and the Shipment Booked. A rejection carries a reason code (required) and, optionally, a reason text: the "
                + "booking becomes Rejected and the Shipment goes back to Planned. Either way, the matching event is published on the Shipment "
                + "events topic in the same transaction; the Shipment's move publishes no event of its own. This build covers only the happy "
                + "path: a booking that isn't Requested, an unknown booking number, field validation, idempotent repeats and the wrong Carrier "
                + "answering are later tickets.");

        return app;
    }

    private static async Task<Results<Ok<CarrierBookingAnswerResult>, ValidationProblem>> AnswerAsync(
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

        return TypedResults.Ok(result.Value);
    }

    /// <summary>
    /// The call's errors: an <see cref="TransportOrderEndpoints.CorrelationIdHeader"/>, a confirmation's Carrier booking
    /// reference, a rejection's reason code. Whether the reason code is one of the known ones, and whether <c>Other</c>
    /// has its text, are a later ticket's field validation.
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

        return errors;
    }
}
