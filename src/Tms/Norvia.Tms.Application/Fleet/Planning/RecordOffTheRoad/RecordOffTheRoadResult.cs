namespace Norvia.Tms.Application.Fleet.Planning.RecordOffTheRoad;

/// <param name="Days">How many days the Vehicle is now Off the road because of this action.</param>
public sealed record RecordOffTheRoadResult(DateOnly From, DateOnly To, int Days);
