namespace Norvia.PartnerPortal.Domain.CarrierBookings;

/// <summary>Which Tracking events a Carrier Persona may send next for a Shipment, as the P5 portal section has them.</summary>
public static class TrackingRules
{
    /// <summary>The progress events a Persona in auto-progress sends in turn: Arrived is left out, as some Carriers never send it.</summary>
    private static readonly TrackingEventType[] AutoProgressInOrder =
        [TrackingEventType.Collected, TrackingEventType.Departed, TrackingEventType.OutForDelivery, TrackingEventType.Delivered];

    private static readonly TrackingEventType[] ProgressInOrder =
        [TrackingEventType.Collected, TrackingEventType.Departed, TrackingEventType.Arrived, TrackingEventType.OutForDelivery, TrackingEventType.Delivered];

    public static bool IsProgress(TrackingEventType type) => type <= TrackingEventType.Delivered;

    /// <summary>The stage a progress event moves a Shipment to; null for an exception, which never changes it.</summary>
    public static ShipmentStage? StageAfter(TrackingEventType type) => type switch
    {
        TrackingEventType.Collected => ShipmentStage.Collected,
        TrackingEventType.Departed or TrackingEventType.Arrived or TrackingEventType.OutForDelivery => ShipmentStage.InTransit,
        TrackingEventType.Delivered => ShipmentStage.Delivered,
        _ => null,
    };

    /// <summary>The stage a Shipment is at once <paramref name="lastProgress"/>, the last progress event sent, has gone out.</summary>
    public static ShipmentStage StageOf(TrackingEventType? lastProgress) => lastProgress is { } type ? StageAfter(type) ?? ShipmentStage.Booked : ShipmentStage.Booked;

    /// <summary>
    /// The progress events that are valid next, in order: the next step, Out for delivery in place of Arrived, which some
    /// Carriers never send, and Delivered at any time, since some Carriers send only the final event. Nothing once the
    /// Shipment is Delivered.
    /// </summary>
    public static IReadOnlyList<TrackingEventType> NextProgress(TrackingEventType? lastProgress)
    {
        if (lastProgress == TrackingEventType.Delivered)
        {
            return [];
        }

        var next = ProgressInOrder[lastProgress is { } last ? Array.IndexOf(ProgressInOrder, last) + 1 : 0];
        return next switch
        {
            TrackingEventType.Delivered => [next],
            TrackingEventType.Arrived => [next, TrackingEventType.OutForDelivery, TrackingEventType.Delivered],
            _ => [next, TrackingEventType.Delivered],
        };
    }

    /// <summary>The progress event a Persona in auto-progress sends after <paramref name="lastProgress"/>; null once the Shipment is Delivered.</summary>
    public static TrackingEventType? NextAutomatic(TrackingEventType? lastProgress) =>
        AutoProgressInOrder.Where(type => lastProgress is null || type > lastProgress).Select(type => (TrackingEventType?)type).FirstOrDefault();

    /// <summary>The exception events offered: Collection failed only while Booked, none once Delivered.</summary>
    public static IReadOnlyList<TrackingEventType> Exceptions(TrackingEventType? lastProgress) => StageOf(lastProgress) switch
    {
        ShipmentStage.Booked => [TrackingEventType.CollectionFailed, TrackingEventType.DeliveryFailed, TrackingEventType.Damaged],
        ShipmentStage.Delivered => [],
        _ => [TrackingEventType.DeliveryFailed, TrackingEventType.Damaged],
    };

    public static bool IsOffered(TrackingEventType? lastProgress, TrackingEventType type) =>
        NextProgress(lastProgress).Contains(type) || Exceptions(lastProgress).Contains(type);
}
