namespace Norvia.Tms.Domain.Trips;

/// <summary>Why the TMS refused a Planner's action on a Trip: a stable code and a readable message.</summary>
public sealed record TripRefusal(TripRefusalCode Code, string Message)
{
    public static TripRefusal TripNotFound(string number) =>
        new(TripRefusalCode.TripNotFound, $"There is no Trip {number}.");

    public static TripRefusal TripNotDraft(TripNumber number) =>
        new(TripRefusalCode.TripNotDraft, $"Trip {number} is Dispatched, so it is no longer a Draft.");
}
