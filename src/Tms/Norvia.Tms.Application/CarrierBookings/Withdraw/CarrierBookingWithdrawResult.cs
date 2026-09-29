using Norvia.Tms.Domain.CarrierBookings;
using Norvia.Tms.Domain.Shipments;

namespace Norvia.Tms.Application.CarrierBookings.Withdraw;

/// <summary>What the Planner's withdrawal of a Carrier booking changed: its new status and the Shipment's.</summary>
/// <param name="Repeat">True when the same withdrawal was sent again under its Idempotency-Key, answered as before without withdrawing again.</param>
public sealed record CarrierBookingWithdrawResult(
    string CarrierBookingNumber, CarrierBookingStatus Status, string ShipmentNumber, ShipmentStatus ShipmentStatus, string CorrelationId, bool Repeat = false);
