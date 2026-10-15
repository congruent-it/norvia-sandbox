namespace Norvia.Simulator.Domain.Carriers;

/// <summary>
/// What becomes of one Shipment assigned to a Carrier: whether the Planner releases it, how the Carrier answers, and what the
/// Carrier then reports. <see cref="StopsAt"/> is the last progress event of a tracked Shipment that never reaches the
/// delivery, and null for one that is delivered or fails.
/// </summary>
public sealed record CarrierJourney(
    int Index,
    bool Released,
    CarrierAnswerKind Answer,
    bool Tracked,
    CarrierExceptionKind? Exception,
    bool Delivered,
    bool ProofOfDelivery,
    bool Cancel,
    CarrierEvent? StopsAt)
{
    private static readonly CarrierEvent[] Progress =
        [CarrierEvent.Collected, CarrierEvent.Departed, CarrierEvent.Arrived, CarrierEvent.OutForDelivery, CarrierEvent.Delivered];

    /// <summary>The tracking events the Carrier reports, in the order it reports them.</summary>
    public IReadOnlyList<CarrierEvent> Events
    {
        get
        {
            if (!Tracked)
            {
                return [];
            }

            if (Exception == CarrierExceptionKind.CollectionFailed)
            {
                return [CarrierEvent.CollectionFailed];
            }

            var events = Progress.TakeWhile(step => step != CarrierEvent.Delivered || Delivered).ToList();
            if (Exception == CarrierExceptionKind.DeliveryFailed)
            {
                events.Add(CarrierEvent.DeliveryFailed);
            }
            else if (!Delivered && StopsAt is { } last)
            {
                events = [.. events.TakeWhile(step => step != last), last];
            }

            if (Exception == CarrierExceptionKind.Damaged)
            {
                // Damage is found on the way: after the departure when the Shipment got that far.
                events.Insert(events.Contains(CarrierEvent.Departed) ? events.IndexOf(CarrierEvent.Departed) + 1 : events.Count, CarrierEvent.Damaged);
            }

            return events;
        }
    }
}
