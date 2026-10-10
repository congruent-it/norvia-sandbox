namespace Norvia.Tms.Application.Trips;

/// <summary>Something the Planner should know about a Trip, which doesn't stop it: a stable code and a readable message.</summary>
public sealed record TripWarning(string Code, string Message)
{
    public const string DeliveryWindowOutsideTripDay = nameof(DeliveryWindowOutsideTripDay);

    public static TripWarning DeliveryWindowOutside(string shipmentNumber, DateOnly day) =>
        new(DeliveryWindowOutsideTripDay, $"Shipment {shipmentNumber}'s delivery window doesn't include the Trip's day {day:yyyy-MM-dd}.");
}
