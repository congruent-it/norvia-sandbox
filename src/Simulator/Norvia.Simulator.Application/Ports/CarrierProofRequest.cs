using Norvia.Simulator.Domain.Plans;

namespace Norvia.Simulator.Application.Ports;

/// <summary>
/// A Carrier's Proof of delivery: who received the goods, when and where, in what condition, with its document. The Carrier name and the
/// Shipment and Transport Order numbers are only for that document: the Partner API identifies the Carrier by its token.
/// </summary>
public sealed record CarrierProofRequest(
    string CarrierName, string ShipmentNumber, string TransportOrderNumber, string BookingReference, string CarrierBookingNumber, string DocumentId, string ReceivedBy, DateTimeOffset At, PlannedAddress Place, bool Reservations, string? Remark);
