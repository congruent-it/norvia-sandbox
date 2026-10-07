using Norvia.Tms.Application.Shipments;
using Norvia.Tms.Application.TransportOrders;
using Norvia.Tms.Domain.Exceptions;
using Norvia.Tms.Domain.ShipperNotifications;

namespace Norvia.Tms.Application.Exceptions.ListOpen;

/// <summary>An Open Exception, in the Planner's list of Open Exceptions (P7).</summary>
/// <param name="CarriedBy">Who carried the Shipment when the Exception opened: the Carrier, or Own fleet.</param>
/// <param name="CanCancelCarrierBooking">Whether the Shipment is a Carrier Shipment that is Booked, not yet Collected, so the Planner can cancel its Carrier booking.</param>
/// <param name="CanAgreeDeliveryWindow">Whether the Shipment is Collected or In transit, so the Planner can agree a delivery window.</param>
/// <param name="NumberOfCauses">How many causes it has: the one that opened it and every later one of its type.</param>
/// <param name="AfterDelivered">Whether the Shipment was Delivered already when the Exception opened.</param>
/// <param name="CauseCleared">Whether the cause no longer holds. The Exception stays Open until the Planner resolves it.</param>
/// <param name="Notes">The Planner's notes, in the order they were added.</param>
/// <param name="LastShipperNotificationStatus">Where the last Shipper notification stands, so a Shipper that wasn't told shows; null when the Planner hasn't notified the Shipper.</param>
public sealed record OpenExceptionSummary(
    string ExceptionNumber,
    string ShipmentNumber,
    ShipperView Shipper,
    CarriedByView CarriedBy,
    ExceptionType Type,
    DateTimeOffset OpenedAt,
    bool CanCancelCarrierBooking,
    bool CanAgreeDeliveryWindow,
    int NumberOfCauses,
    bool AfterDelivered,
    bool CauseCleared,
    IReadOnlyList<ExceptionNoteSummary> Notes,
    ShipperNotificationStatus? LastShipperNotificationStatus);
