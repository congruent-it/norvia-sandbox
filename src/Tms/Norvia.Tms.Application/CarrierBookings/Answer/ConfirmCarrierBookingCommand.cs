using Ardalis.Result;
using Mediator;

namespace Norvia.Tms.Application.CarrierBookings.Answer;

/// <summary>
/// The Carrier confirms a Requested Carrier booking with its own booking reference and, optionally, a planned collection
/// time (P4). The booking becomes Confirmed and the Shipment Booked, and <c>CarrierBookingConfirmed</c> is published, in
/// one transaction. A confirmation that exactly repeats the one already recorded succeeds as a repeat, changing and
/// publishing nothing; a different answer to a booking that already has one is refused with <c>BookingNotOpen</c>.
/// </summary>
/// <param name="CarrierId">
/// The Carrier answering. API Management's token check will take it from the token (ADR 0005); the caller passes it
/// directly until then.
/// </param>
/// <param name="CorrelationId">
/// The answer's own Message trail. API Management will create it once its token check exists; the caller passes it
/// directly until then.
/// </param>
public sealed record ConfirmCarrierBookingCommand(
    string CarrierBookingNumber,
    string CarrierId,
    string CorrelationId,
    string CarrierBookingReference,
    DateTimeOffset? PlannedCollectionTime)
    : ICommand<Result<CarrierBookingAnswerResult>>, ICarrierBookingAnswerCommand;
