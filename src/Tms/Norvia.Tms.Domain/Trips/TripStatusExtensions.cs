namespace Norvia.Tms.Domain.Trips;

public static class TripStatusExtensions
{
    /// <summary>True while the Planner can still change the Trip: a Draft, or a Dispatched Trip by Amend. Once it is In progress only taking off a Shipment is allowed.</summary>
    public static bool CanBeAmended(this TripStatus status) => status is TripStatus.Draft or TripStatus.Dispatched;
}
