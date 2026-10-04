using Norvia.Tms.Domain.Shipments;
using static System.FormattableString;

namespace Norvia.Tms.Domain.TrackingEvents;

/// <summary>P5's Tracking event rules, apart from finding the Shipment by the Carrier's booking reference.</summary>
/// <param name="ClockSkew">How far in the future an event time may be, to allow for the reporter's clock running ahead.</param>
public sealed record TrackingEventRules(TimeSpan ClockSkew)
{
    public static TrackingEventRefusal ShipmentNotFound(string carrierBookingReference) =>
        new(TrackingEventRefusalCode.ShipmentNotFound, Invariant($"No Confirmed Carrier booking of this Carrier has the booking reference {carrierBookingReference}."));

    /// <summary>The refusal for an event time later than now plus the clock skew, or null when it isn't.</summary>
    public TrackingEventRefusal? CheckEventTime(DateTimeOffset eventTime, DateTimeOffset now) =>
        eventTime > now + ClockSkew
            ? new(TrackingEventRefusalCode.EventTimeInTheFuture, Invariant($"The event time {eventTime:O} is in the future."))
            : null;

    /// <summary>The refusal for a Shipment that isn't Booked or later, or null when it is.</summary>
    public static TrackingEventRefusal? CheckShipment(Shipment shipment)
    {
        ArgumentNullException.ThrowIfNull(shipment);
        return shipment.Status >= ShipmentStatus.Booked
            ? null
            : new(TrackingEventRefusalCode.ShipmentNotInTransport, Invariant($"Shipment {shipment.Number} is {shipment.Status}, not Booked or later."));
    }
}
