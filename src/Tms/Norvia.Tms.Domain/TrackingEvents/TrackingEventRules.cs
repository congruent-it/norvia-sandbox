using Norvia.Tms.Domain.Shipments;
using Norvia.Tms.Domain.Trips;
using static System.FormattableString;

namespace Norvia.Tms.Domain.TrackingEvents;

/// <summary>P5's Tracking event rules, apart from finding the Shipment by the Carrier's booking reference.</summary>
/// <param name="ClockSkew">How far in the future an event time may be, to allow for the reporter's clock running ahead.</param>
public sealed record TrackingEventRules(TimeSpan ClockSkew)
{
    // The Driver app has no planned times per Stop and reads no GPS, so it sends no Departed, Arrived or Out for delivery (P12).
    private static readonly TrackingEventType[] OwnFleetTypes =
        [TrackingEventType.Collected, TrackingEventType.CollectionFailed, TrackingEventType.Delivered, TrackingEventType.DeliveryFailed, TrackingEventType.Damaged];

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

    /// <summary>
    /// The refusal for an Own fleet report (P12) that breaks a Driver report rule, or null when it breaks none. <paramref name="trip"/> is the
    /// Trip the report names and <paramref name="shipment"/> the Shipment it names, each null when there is none. The Driver comes from the
    /// token, so a Trip that is another Driver's is no Trip found. The rules are checked in this order, and a report is only about the Shipment
    /// being on the Trip's latest version, whatever version the report named.
    /// </summary>
    public TrackingEventRefusal? CheckOwnFleetReport(Trip? trip, Shipment? shipment, TrackingReport report, StopOutcomes outcomes, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(report);
        ArgumentNullException.ThrowIfNull(outcomes);
        var own = report.OwnFleet ?? throw new ArgumentException("An Own fleet report names its Trip.", nameof(report));

        if (trip is null || trip.DriverId != own.DriverId)
        {
            return new(TrackingEventRefusalCode.TripNotFound, Invariant($"Driver {own.DriverId} has no Trip {own.TripNumber}."));
        }

        if (!OwnFleetTypes.Contains(report.Type))
        {
            return new(TrackingEventRefusalCode.UnknownEventType, Invariant($"A Driver reports {string.Join(", ", OwnFleetTypes)}, not {report.Type}."));
        }

        if (trip.Status != TripStatus.InProgress)
        {
            return new(TrackingEventRefusalCode.TripNotUnderWay, Invariant($"Trip {trip.Number} is {trip.Status}, not In progress."));
        }

        if (shipment is null || !trip.HasShipment(shipment.Id))
        {
            return new(TrackingEventRefusalCode.ShipmentNotOnTrip, Invariant($"Shipment {own.ShipmentNumber} is not on Trip {trip.Number}, whose latest version is {trip.Version}."));
        }

        if (StopOutcomes.StopOf(report.Type) is { } stop)
        {
            if (!outcomes.Of(shipment.Id, stop).IsOpen)
            {
                return new(TrackingEventRefusalCode.StopAlreadyCompleted, Invariant($"The {stop} Stop of Shipment {shipment.Number} on Trip {trip.Number} has its outcome already."));
            }

            if (stop == StopKind.Delivery && outcomes.Of(shipment.Id, StopKind.Collection).Kind != StopOutcomeKind.Collected)
            {
                return new(TrackingEventRefusalCode.CollectionBeforeDelivery, Invariant($"Shipment {shipment.Number} can't be delivered before it is collected."));
            }
        }

        return CheckEventTime(report.EventTime, now);
    }
}
