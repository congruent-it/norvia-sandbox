using Ardalis.Result;
using Mediator;
using Norvia.Tms.Domain.CarrierBookings;

namespace Norvia.Tms.Application.CarrierBookings.Answer;

/// <summary>
/// The Carrier rejects a Requested Carrier booking with a reason (P4). The booking becomes Rejected and the Shipment
/// goes back to Planned, and <c>CarrierBookingRejected</c> is published, in one transaction; the Shipment's move
/// publishes no event of its own. A rejection that exactly repeats the one already recorded succeeds as a repeat,
/// changing and publishing nothing; a different answer to a booking that already has one is refused with
/// <c>BookingNotOpen</c>.
/// </summary>
/// <param name="CarrierId">
/// The Carrier answering. API Management's token check will take it from the token (ADR 0005); the caller passes it
/// directly until then.
/// </param>
/// <param name="CorrelationId">
/// The answer's own Message trail. API Management will create it once its token check exists; the caller passes it
/// directly until then.
/// </param>
public sealed record RejectCarrierBookingCommand(
    string CarrierBookingNumber,
    string CarrierId,
    string CorrelationId,
    CarrierBookingRejectionReason ReasonCode,
    string? ReasonText)
    : ICommand<Result<CarrierBookingAnswerResult>>, ICarrierBookingAnswerCommand;
