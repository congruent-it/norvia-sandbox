using Ardalis.Result;
using Mediator;

namespace Norvia.Tms.Application.CarrierBookings.Withdraw;

/// <summary>
/// The Planner withdraws a Requested Carrier booking, from the TMS UI (P4), with the same authorization as P3's other
/// planning commands. The booking becomes Withdrawn and the Shipment goes back to Planned, and CarrierBookingWithdrawn is
/// published, in one transaction; the Shipment's move carries no event of its own. A later answer for it is refused with
/// BookingNotOpen. A withdrawal sent again under its Idempotency-Key is answered as before, flagged as a repeat, without
/// withdrawing again; a withdrawal of a booking that already has a different outcome is refused with BookingNotOpen.
/// </summary>
/// <param name="WithdrawalKey">The caller's idempotency key, so a retry of the withdrawal gets the same answer instead of a refusal.</param>
/// <param name="CorrelationId">The new Message trail the TMS starts for this withdrawal, as it starts in the TMS UI.</param>
public sealed record WithdrawCarrierBookingCommand(string CarrierBookingNumber, string WithdrawalKey, string CorrelationId)
    : ICommand<Result<CarrierBookingWithdrawResult>>;
