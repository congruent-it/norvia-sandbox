using static System.FormattableString;

namespace Norvia.Tms.Domain.Trips;

/// <summary>Why the TMS refused a Planner's action on a Trip: a stable code and a readable message.</summary>
public sealed record TripRefusal(TripRefusalCode Code, string Message)
{
    public static TripRefusal TripNotFound(string number) =>
        new(TripRefusalCode.TripNotFound, $"There is no Trip {number}.");

    public static TripRefusal TripNotDraft(TripNumber number) =>
        new(TripRefusalCode.TripNotDraft, $"Trip {number} is Dispatched, so it is no longer a Draft.");

    public static TripRefusal OutsideShift(string driverId, TimeOnly start, DateOnly day, TimeOnly shiftStart, TimeOnly shiftEnd) =>
        new(TripRefusalCode.OutsideShift, Invariant($"A planned start of {start:HH\\:mm} is outside Driver {driverId}'s Shift on {day:yyyy-MM-dd} ({shiftStart:HH\\:mm}-{shiftEnd:HH\\:mm})."));

    public static TripRefusal ShipmentNotFound(string number) =>
        new(TripRefusalCode.ShipmentNotFound, $"There is no Shipment {number}.");

    public static TripRefusal ShipmentNotReleased(string number, string reason) =>
        new(TripRefusalCode.ShipmentNotReleased, $"Shipment {number} can't go on a Trip: {reason}.");

    public static TripRefusal ShipmentOnAnotherTrip(string number, TripNumber trip) =>
        new(TripRefusalCode.ShipmentOnAnotherTrip, $"Shipment {number} is on Trip {trip} already.");

    public static TripRefusal CollectionDayMismatch(string number, DateOnly? collectionDay, DateOnly tripDay) =>
        new(TripRefusalCode.CollectionDayMismatch, Invariant($"Shipment {number} collects on {(collectionDay is { } day ? day.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture) : "no known day")}, not on the Trip's day {tripDay:yyyy-MM-dd}."));
}
