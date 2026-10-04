namespace Norvia.Tms.Domain.TrackingEvents;

/// <summary>
/// What a Tracking event reports (P5). Only the types the TMS can record so far are here; each later P5 ticket adds its own
/// (Departed, Arrived, Out for delivery, Delivered and the exceptions). The names are stable: never rename one, only add.
/// </summary>
public enum TrackingEventType
{
    /// <summary>The goods have been picked up. Moves a Booked Shipment to Collected.</summary>
    Collected = 1,
}
