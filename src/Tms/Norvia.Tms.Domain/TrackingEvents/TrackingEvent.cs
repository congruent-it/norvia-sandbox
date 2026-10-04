using Ardalis.SharedKernel;
using Norvia.Tms.Domain.Shipments;
using Norvia.Tms.Domain.TransportOrders;

namespace Norvia.Tms.Domain.TrackingEvents;

/// <summary>
/// One report from a Carrier (or, later, a Driver) that something happened to a Shipment, at a given time and place (P5).
/// The TMS keeps every one it accepts, as a log on the Shipment, apart from what it concluded from it: whether the event
/// moved the Shipment's status is <see cref="MovedShipment"/>. The TMS owns it. It is made only by a Shipment taking an
/// event in (<see cref="Shipment.Record"/>), and never changed or deleted.
/// </summary>
public sealed class TrackingEvent : EntityBase<TrackingEventId>, IAggregateRoot
{
    private TrackingEvent(
        TrackingEventId id,
        ShipmentId shipmentId,
        ShipmentNumber shipmentNumber,
        TrackingSource source,
        string sourceEventId,
        string? carrierBookingReference,
        TrackingEventType type,
        DateTimeOffset eventTime,
        DateTimeOffset? eta,
        string? remark,
        CorrelationId correlationId,
        DateTimeOffset receivedAt)
    {
        Id = id;
        ShipmentId = shipmentId;
        ShipmentNumber = shipmentNumber;
        Source = source;
        SourceEventId = sourceEventId;
        CarrierBookingReference = carrierBookingReference;
        Type = type;
        EventTime = eventTime;
        Eta = eta;
        Remark = remark;
        CorrelationId = correlationId;
        ReceivedAt = receivedAt;
    }

    public ShipmentId ShipmentId { get; private set; }

    public ShipmentNumber ShipmentNumber { get; private set; }

    public TrackingSource Source { get; private set; }

    /// <summary>The reporter's own ID for the event, unique per source: a repeat of it is recognised as one.</summary>
    public string SourceEventId { get; private set; }

    /// <summary>The Carrier's own reference for the booking the event was matched by; null for an Own fleet event.</summary>
    public string? CarrierBookingReference { get; private set; }

    public TrackingEventType Type { get; private set; }

    /// <summary>When it happened, with the offset of where it happened.</summary>
    public DateTimeOffset EventTime { get; private set; }

    public TrackingLocation Location { get; private set; } = null!; // set when made, and a complex property, which a constructor can't bind

    public DateTimeOffset? Eta { get; private set; }

    public string? Remark { get; private set; }

    /// <summary>The Message trail of the call that reported it, which API Management creates.</summary>
    public CorrelationId CorrelationId { get; private set; }

    /// <summary>When the TMS recorded it, apart from when it happened.</summary>
    public DateTimeOffset ReceivedAt { get; private set; }

    /// <summary>Whether the event moved the Shipment's status; an event that doesn't is only logged.</summary>
    public bool MovedShipment { get; private set; }

    internal static TrackingEvent Record(Shipment shipment, TrackingReport report, CorrelationId correlationId, DateTimeOffset receivedAt)
    {
        ArgumentNullException.ThrowIfNull(shipment);
        ArgumentNullException.ThrowIfNull(report);
        ArgumentException.ThrowIfNullOrWhiteSpace(report.EventId);
        ArgumentNullException.ThrowIfNull(report.Source);
        ArgumentNullException.ThrowIfNull(report.Location);
        ArgumentNullException.ThrowIfNull(correlationId);
        return new TrackingEvent(
            TrackingEventId.New(),
            shipment.Id,
            shipment.Number,
            report.Source,
            report.EventId,
            report.CarrierBookingReference,
            report.Type,
            report.EventTime,
            report.Eta,
            report.Remark,
            correlationId,
            receivedAt)
        {
            Location = report.Location,
        };
    }

    internal void MarkMovedShipment() => MovedShipment = true;
}
