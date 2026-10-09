using Ardalis.SharedKernel;
using Norvia.Tms.Domain.Shipments;
using Norvia.Tms.Domain.TrackingEvents;

namespace Norvia.Tms.Domain.CarrierBookings;

/// <summary>
/// A Carrier Shipment moved to Delivered, so the Carrier has earned the Carrier cost frozen on the Confirmed Carrier booking
/// that carried it (P9). It doesn't wait for a Proof of delivery or for Billable.
/// </summary>
public sealed class CarrierCostIncurredEvent(Shipment shipment, CarrierBooking booking, CarrierCost carrierCost, TrackingEvent deliveredEvent) : DomainEventBase
{
    public Shipment Shipment { get; } = shipment;

    public CarrierBooking Booking { get; } = booking;

    public CarrierCost CarrierCost { get; } = carrierCost;

    /// <summary>The Delivered Tracking event that moved the Shipment: its time is the delivery time, and its correlation ID the event's.</summary>
    public TrackingEvent DeliveredEvent { get; } = deliveredEvent;
}
