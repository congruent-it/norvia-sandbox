using Ardalis.Result;
using Mediator;

namespace Norvia.Tms.Application.Exceptions.CancelCarrierBooking;

/// <summary>
/// From an open Exception the Planner cancels the Confirmed Carrier booking of a Booked Shipment with a reason (P7, ADR 0010).
/// The action starts in the TMS UI, so its correlation ID is the TMS's own. The cancellation sent again under its key is a
/// repeat, answered as before.
/// </summary>
/// <param name="CancellationKey">The idempotency key that makes a retry of the cancellation a repeat.</param>
public sealed record CancelCarrierBookingCommand(string ExceptionNumber, string ReasonText, string CancellationKey, string CorrelationId)
    : ICommand<Result<CancelCarrierBookingResult>>;
