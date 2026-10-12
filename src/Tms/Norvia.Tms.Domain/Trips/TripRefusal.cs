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

    public static TripRefusal CollectionDayMismatch(string number, DateOnly? collectionDay, DateOnly tripDay)
    {
        var collects = collectionDay is { } day ? Invariant($"{day:yyyy-MM-dd}") : "no known day";
        return new(TripRefusalCode.CollectionDayMismatch, Invariant($"Shipment {number} collects on {collects}, not on the Trip's day {tripDay:yyyy-MM-dd}."));
    }

    public static TripRefusal CollectionBeforeDelivery(string number) =>
        new(TripRefusalCode.CollectionBeforeDelivery, $"Shipment {number}'s delivery can't come before its collection.");

    public static TripRefusal StopsNotTheTripsStops(TripNumber trip) =>
        new(TripRefusalCode.StopsNotTheTripsStops, $"The order must name each Stop of Trip {trip} once, and no others.");

    public static TripRefusal TripIncomplete(TripNumber trip, string part) =>
        new(TripRefusalCode.TripIncomplete, $"Trip {trip} can't be dispatched without {part}.");

    public static TripRefusal TripNotDispatched(TripNumber number, TripStatus status) =>
        new(TripRefusalCode.TripNotDispatched, status == TripStatus.Draft
            ? $"Trip {number} is a Draft, so no version of it has been sent."
            : $"Trip {number} is {status}, not Dispatched.");

    public static TripRefusal NotTripDay(TripNumber number, DateOnly tripDay, DateOnly today) =>
        new(TripRefusalCode.NotTripDay, Invariant($"Trip {number} is for {tripDay:yyyy-MM-dd}, and today is {today:yyyy-MM-dd}."));

    public static TripRefusal TripNotUnderWay(TripNumber number, TripStatus status) =>
        new(TripRefusalCode.TripNotUnderWay, $"Trip {number} is {status}, not In progress.");

    public static TripRefusal StopsOpen(TripNumber number, int open) =>
        new(TripRefusalCode.StopsOpen, Invariant($"Trip {number} can't end while {open} of its Stops have no outcome."));

    public static TripRefusal TripCompleted(TripNumber number) =>
        new(TripRefusalCode.TripCompleted, $"Trip {number} is completed, so it can't be changed.");

    public static TripRefusal TripVersionNotFound(TripNumber number, int version) =>
        new(TripRefusalCode.TripVersionNotFound, Invariant($"Trip {number} has no version {version}."));

    public static TripRefusal TripVersionNotLatest(TripNumber number, int version, int latest) =>
        new(TripRefusalCode.TripVersionNotLatest, Invariant($"Version {version} of Trip {number} is not its latest, which is version {latest}."));

    public static TripRefusal NothingToSendAgain(TripNumber number, int version, TripDeliveryState state) =>
        new(TripRefusalCode.NothingToSendAgain, Invariant($"Version {version} of Trip {number} is {state}, so there is nothing to send again."));

    public static TripRefusal VehicleOrDriverInactive(TripNumber number) =>
        new(TripRefusalCode.VehicleOrDriverInactive, $"The Vehicle or the Driver of Trip {number} is no longer active, so it can't be sent again as it is.");

    public static TripRefusal TripUnderWay(TripNumber number) =>
        new(TripRefusalCode.TripUnderWay, $"Trip {number} is in progress, so the only change allowed is taking off a Shipment that hasn't been collected.");

    public static TripRefusal ShipmentNotOnTrip(string number, TripNumber trip) =>
        new(TripRefusalCode.ShipmentNotOnTrip, $"Shipment {number} is not on Trip {trip}.");

    public static TripRefusal ShipmentCollected(string number, TripNumber trip) =>
        new(TripRefusalCode.ShipmentCollected, $"Shipment {number} is collected already, so it can't be taken off Trip {trip}. A failed delivery goes through P7.");
}
