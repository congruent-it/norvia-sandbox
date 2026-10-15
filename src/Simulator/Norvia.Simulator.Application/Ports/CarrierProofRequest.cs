using Norvia.Simulator.Domain.Plans;

namespace Norvia.Simulator.Application.Ports;

/// <summary>A Carrier's Proof of delivery: who received the goods, when and where, in what condition, with its document.</summary>
public sealed record CarrierProofRequest(
    string BookingReference, string CarrierBookingNumber, string DocumentId, string ReceivedBy, DateTimeOffset At, PlannedAddress Place, bool Reservations, string? Remark);
