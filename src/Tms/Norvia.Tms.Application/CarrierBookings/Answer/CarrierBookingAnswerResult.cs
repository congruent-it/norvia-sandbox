using Norvia.Tms.Domain.CarrierBookings;
using Norvia.Tms.Domain.Shipments;

namespace Norvia.Tms.Application.CarrierBookings.Answer;

/// <summary>What recording the Carrier's answer changed: the Carrier booking's new status and the Shipment's.</summary>
public sealed record CarrierBookingAnswerResult(
    string CarrierBookingNumber,
    CarrierBookingStatus Status,
    string ShipmentNumber,
    ShipmentStatus ShipmentStatus,
    string CorrelationId);
